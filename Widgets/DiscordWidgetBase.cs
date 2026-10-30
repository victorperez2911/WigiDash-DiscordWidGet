using Microsoft.Extensions.Logging;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Discord;
using WigiDash.DiscordWidget.Rendering;
using WigiDash.DiscordWidget.Settings;

namespace WigiDash.DiscordWidget.Widgets;

/// <summary>
/// Base dos widgets do Discord. Cada função (Mute, Deafen...) é um widget próprio no catálogo; todos usam a
/// mesma <see cref="DiscordSession"/>.
/// </summary>
public abstract class DiscordWidgetBase : IWidget
{
    protected const string Website = "https://github.com/victorperez2911/WigiDash-DiscordWidGet";

    private IWidgetHost? _host;

    public abstract WidgetManifest Manifest { get; }

    /// <summary>O botão como aparece no catálogo.</summary>
    protected abstract ButtonVisual Preview { get; }

    protected abstract DiscordButtonInstance CreateInstance(IWidgetContext context);

    public ValueTask LoadAsync(IWidgetHost host, CancellationToken cancellationToken)
    {
        _host = host;
        DiscordSession.Shared.UseLogger(host.LoggerFactory);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<IWidgetInstance> CreateInstanceAsync(IWidgetContext context, CancellationToken cancellationToken)
    {
        var instance = CreateInstance(context);
        await instance.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        return instance;
    }

    public ValueTask<WidgetFrame?> GetPreviewAsync(WidgetSize size, CancellationToken cancellationToken) =>
        ValueTask.FromResult<WidgetFrame?>(ButtonRenderer.Render(size.PixelSize, _host?.Theme ?? new WidgetTheme(), Preview));

    /// <summary>Ficha padrão: botão de 1x1 ou 2x1, versão do plugin.</summary>
    protected static WidgetManifest CreateManifest(Guid id, string name, string description) => new()
    {
        Id = id,
        Name = name,
        Version = typeof(DiscordWidgetBase).Assembly.GetName().Version ?? new Version(2, 0, 0),
        Author = "Victor Perez",
        Website = Website,
        Description = description,
        SupportedSizes = [WidgetSize.Small, WidgetSize.Wide],
    };
}

/// <summary>
/// Instância de um botão do Discord: desenha o estado da sessão (desconectado, falta autorizar) até ela ficar
/// pronta, e só aí repassa o desenho e os toques para o widget concreto.
/// </summary>
public abstract class DiscordButtonInstance : IWidgetInstance
{
    private readonly IDisposable _lease;

    protected DiscordButtonInstance(IWidgetContext context)
    {
        Context = context;
        Session.Changed += OnSessionChanged;
        AvatarCache.Loaded += OnSessionChanged;
        _lease = Session.Acquire();
    }

    protected IWidgetContext Context { get; }

    /// <summary>Os testes trocam pela sessão ligada ao Discord simulado.</summary>
    internal static DiscordSession? SessionOverride { get; set; }

    public DiscordSession Session { get; } = SessionOverride ?? DiscordSession.Shared;

    /// <summary>Algo que a tela de configurações mostra mudou (sessão ou configuração do widget).</summary>
    public event Action? Changed;

    /// <summary>Ícone e nome do widget, usados também nos estados de sessão.</summary>
    protected abstract string Glyph { get; }

    protected abstract string Label { get; }

    /// <summary>Componente Razor com as opções próprias do widget (parâmetro <c>Instance</c>), ou <c>null</c>.</summary>
    public virtual Type? OptionsComponent => null;

    public Type? SettingsComponent => typeof(DiscordSettings);

    /// <summary>Lê as configurações da instância antes do primeiro quadro.</summary>
    protected internal virtual ValueTask LoadSettingsAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>Desenho com a sessão pronta.</summary>
    protected abstract ButtonVisual GetVisual();

    /// <summary>Toque (<see cref="TouchEvent.IsGesture"/> ou press/release) com a sessão pronta.</summary>
    protected abstract Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken);

    public ValueTask<WidgetFrame> RenderAsync(RenderReason reason, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ButtonRenderer.Render(Context.PixelSize, Context.Theme, CurrentVisual()));

    public ButtonVisual CurrentVisual()
    {
        var detail = Session.State switch
        {
            DiscordState.Ready => null,
            DiscordState.DiscordNotRunning => "Discord fechado",
            DiscordState.NeedsAuthorization => "Toque para autorizar",
            DiscordState.Authorizing => "Autorize no Discord",
            DiscordState.Failed => "Erro de conexão",
            _ => "Conectando...",
        };
        if (detail is not null)
            return new ButtonVisual(Glyph, Label) { Detail = detail, Dimmed = true };

        var visual = GetVisual();
        if (!_feedbackActive)
            return visual;
        // Retorno do toque por cima do desenho normal: verde com a barra esvaziando, ou vermelho se falhou.
        var elapsed = Context.Host.Time.GetUtcNow() - _feedbackStart;
        var remaining = 1 - Math.Clamp(elapsed / _feedbackLength, 0, 1);
        return visual with
        {
            Accent = _feedbackOk ? ButtonRenderer.Success : ButtonRenderer.Danger,
            Detail = _feedbackText ?? visual.Detail,
            Dimmed = false,
            Level = remaining,
        };
    }

    private static readonly TimeSpan FeedbackFrame = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan FailureFeedback = TimeSpan.FromSeconds(2);

    /// <summary>Duração padrão do retorno de sucesso.</summary>
    protected static readonly TimeSpan DefaultFeedback = TimeSpan.FromMilliseconds(800);

    private DateTimeOffset _feedbackStart;
    private TimeSpan _feedbackLength;
    private bool _feedbackOk;
    private string? _feedbackText;
    private bool _feedbackActive;

    /// <summary>
    /// Mostra o retorno do toque: verde com uma barra que esvazia em <paramref name="length"/> (sucesso) ou
    /// vermelho (falha). Chame só depois que o Discord respondeu ao comando, nunca por conta própria.
    /// Chamar de novo durante a animação reinicia a contagem.
    /// </summary>
    protected void ShowFeedback(bool success, TimeSpan? length = null, string? text = null)
    {
        _feedbackOk = success;
        _feedbackText = text;
        _feedbackLength = length ?? (success ? DefaultFeedback : FailureFeedback);
        _feedbackStart = Context.Host.Time.GetUtcNow();
        if (_feedbackActive)
            return;
        _feedbackActive = true;
        _ = AnimateFeedbackAsync();
    }

    /// <summary>Roda o comando e mostra o retorno de sucesso quando o Discord confirma.</summary>
    protected async Task ConfirmAsync(Func<Task> command, TimeSpan? length = null)
    {
        await command().ConfigureAwait(false);
        ShowFeedback(true, length);
    }

    private async Task AnimateFeedbackAsync()
    {
        try
        {
            while (Context.Host.Time.GetUtcNow() - _feedbackStart < _feedbackLength)
            {
                Refresh();
                await Task.Delay(FeedbackFrame, Context.Host.Time, Context.Stopping).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _feedbackActive = false;
            Refresh();
        }
    }

    public async ValueTask OnTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (Session.State == DiscordState.NeedsAuthorization)
        {
            if (touch.Kind == TouchKind.Tap)
                Session.RequestAuthorization();
            return;
        }
        if (Session.State == DiscordState.Failed && touch.Kind == TouchKind.Tap)
        {
            Session.Reconnect();
            return;
        }
        if (Session.State != DiscordState.Ready)
            return;

        try
        {
            await OnReadyTouchAsync(touch, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DiscordRpcException or InvalidOperationException or IOException or TimeoutException
                                       or HttpRequestException)
        {
            Context.Logger.LogWarning(ex, "Discord: comando de {Widget} falhou", Label);
            ShowFeedback(false, text: FailureText(ex));
        }
    }

    /// <summary>Texto curto do retorno de falha; os widgets podem trocar (ex.: som de outro servidor).</summary>
    protected virtual string FailureText(Exception error) => "Falhou";

    /// <summary>Redesenha e avisa a tela de configurações.</summary>
    protected void Refresh()
    {
        Context.RequestRender();
        Changed?.Invoke();
    }

    private void OnSessionChanged() => Refresh();

    public virtual ValueTask OnSleepAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public virtual ValueTask OnWakeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>Pare timers e tarefas próprias; chamado no <see cref="DisposeAsync"/>.</summary>
    protected virtual void OnDisposing()
    {
    }

    public ValueTask DisposeAsync()
    {
        OnDisposing();
        Session.Changed -= OnSessionChanged;
        AvatarCache.Loaded -= OnSessionChanged;
        _lease.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Botão com opções próprias, guardadas como JSON na chave <c>options</c> das configurações da instância
/// (vão junto ao duplicar o widget).
/// </summary>
public abstract class DiscordOptionsInstance<TOptions>(IWidgetContext context) : DiscordButtonInstance(context)
    where TOptions : class, new()
{
    private const string OptionsKey = "options";

    public TOptions Options { get; private set; } = new();

    protected internal override async ValueTask LoadSettingsAsync(CancellationToken cancellationToken) =>
        Options = await Context.Settings.GetJsonAsync(OptionsKey, new TOptions(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

    public virtual async Task SaveOptionsAsync(TOptions options)
    {
        Options = options;
        await Context.Settings.SetJsonAsync(OptionsKey, options).ConfigureAwait(false);
        Refresh();
    }
}
