# Discord Widget para WigiDash

Widget multi-ação para integração do Discord com o hardware WigiDash da G.SKILL.

## 🎯 Status do Projeto

### ✅ Fase 1: Fundação - COMPLETA

- [x] Estrutura do projeto criada
- [x] GUID único gerado: `3035795C-15EC-4B09-859D-27D1178C949C`
- [x] Implementação base (DiscordWidgetObject e DiscordWidgetInstance)
- [x] Sistema de ações (interfaces e factory)
- [x] Gerenciadores Discord (Gateway, RPC, REST) - estrutura inicial
- [x] Sistema de renderização de bitmaps
- [x] Autenticação OAuth2 com PKCE
- [x] Interface de configuração (XAML)
- [x] Script de deployment
- [x] Build bem-sucedido (0 erros)
- [x] Side loading testado com sucesso

### ⏳ Fase 2: Próximos Passos

1. Criar Discord Application no Developer Portal
2. Configurar OAuth2 e obter Client ID
3. Implementar primeira ação (Voice Channel Status)
4. Testar conectividade Discord

## 🚀 Como Fazer Deploy

### Pré-requisitos

- .NET SDK instalado
- WigiDash Manager instalado
- PowerShell

### Deploy Rápido

```powershell
# Build e deploy (WigiDash deve ser fechado manualmente)
.\deploy.ps1

# Build, deploy e reinicia WigiDash automaticamente
.\deploy.ps1 -RestartWigiDash
```

### Deploy Manual

```powershell
# 1. Build
dotnet build -c Release

# 2. Copiar para pasta do WigiDash
# Destino: %APPDATA%\G.SKILL\WigiDashManager\Widgets\3035795C-15EC-4B09-859D-27D1178C949C\
```

## 📋 Ações Planejadas

| Ação | Tipo | API | Status |
|------|------|-----|--------|
| Voice Channel | Toggle | RPC + Gateway | ⏳ Planejado |
| Soundboard | Button | REST | ⏳ Planejado |
| Mute Toggle | Toggle | RPC | ⏳ Planejado |
| Deafen Toggle | Toggle | RPC | ⏳ Planejado |
| Set Audio Device | Selector | RPC | ⏳ Planejado |
| Server Stats | Display | Gateway | ⏳ Planejado |
| Video Toggle | Toggle | Placeholder | 🔮 Futuro |
| Stream Toggle | Toggle | Placeholder | 🔮 Futuro |

## 🔧 Configuração

### Discord Application

Antes de usar, você precisa:

1. Criar uma Discord Application em https://discord.com/developers/applications
2. Configurar OAuth2 Redirect URI: `http://localhost:8080/callback`
3. Habilitar Gateway Intents:
   - ✅ PRESENCE INTENT
   - ✅ SERVER MEMBERS INTENT (Privilegiado!)
   - ✅ MESSAGE CONTENT INTENT (opcional)
4. Copiar o Client ID
5. Atualizar `DiscordConfig.cs`:

```csharp
public const string CLIENT_ID = "SEU_CLIENT_ID_AQUI";
```

## 📁 Estrutura do Projeto

```
WigiDash-DiscordWidGet/
├── DiscordWidgetObject.cs              # Factory (IWidgetObject)
├── DiscordWidgetInstance.cs            # Instance manager
├── DiscordConfig.cs                    # Configurações
│
├── Core/
│   ├── DiscordGatewayManager.cs        # Discord.Net wrapper
│   ├── DiscordRPCManager.cs            # Discord RPC local
│   └── DiscordRESTClient.cs            # Discord REST API
│
├── Actions/
│   ├── IActionHandler.cs               # Interface de ações
│   ├── ActionContext.cs                # Contexto
│   └── ActionFactory.cs                # Factory
│
├── Rendering/
│   ├── BitmapRenderer.cs               # Renderização
│   └── ImageCache.cs                   # Cache de imagens
│
├── Authentication/
│   ├── OAuth2Handler.cs                # OAuth2 flow
│   └── TokenManager.cs                 # Criptografia de tokens
│
├── UI/
│   ├── DiscordSettingsControl.xaml     # Interface de configuração
│   └── DiscordSettingsControl.xaml.cs
│
└── Resources/
    └── Icons/                          # Ícones das ações
```

## 🔌 Dependências

- **victorperez2911.WigiDashWidgetFramework** 1.1.0 - Framework WigiDash
- **Discord.Net** 2.4.0 - Cliente Discord Gateway
- **Newtonsoft.Json** 13.0.3 - Serialização JSON
- **System.Net.WebSockets.Client** 4.3.2 - WebSocket para RPC local

## 📝 Arquitetura

### Triple-API Integration

O widget usa três APIs do Discord de forma complementar:

1. **Discord Gateway API** (via Discord.Net)
   - Monitoramento de eventos em tempo real
   - Voice state changes
   - Server/channel information

2. **Discord RPC Local** (WebSocket)
   - Controle do cliente Discord instalado
   - Connect/disconnect voice channels
   - Mute/unmute/deafen
   - Audio device selection

3. **Discord REST API**
   - Soundboard (play sounds)
   - Operações HTTP diretas

### Action System

Cada tipo de ação é uma classe independente que implementa `IActionHandler`:

```csharp
public interface IActionHandler
{
    Task InitializeAsync(ActionContext context);
    Task<Bitmap> RenderBitmapAsync(WidgetSize size);
    Task OnClickAsync(int x, int y);
    Task<PrerequisiteResult> ValidatePrerequisitesAsync();
    // ...
}
```

## 🛠️ Desenvolvimento

### Build

```powershell
dotnet build -c Release
```

### Logs

Logs aparecem no WigiDash Manager console e podem ser acessados via `IWidgetManager.WriteLogMessage()`.

### Debug

1. Compile em Debug mode
2. Anexe Visual Studio ao processo WigiDashManager.exe
3. Coloque breakpoints no código

## 📚 Referências

- [Discord API](https://discord.com/developers/docs)
- [Discord.Net Docs](https://docs.discordnet.dev/)
- [Discord RPC](https://discord.com/developers/docs/topics/rpc)

## 📄 Licença

Copyright © 2026 Victor Perez

---

**GUID do Widget:** `3035795C-15EC-4B09-859D-27D1178C949C`
