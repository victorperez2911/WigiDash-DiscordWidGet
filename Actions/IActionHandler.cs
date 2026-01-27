using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Controls;
using WigiDashWidgetFramework.WidgetUtility;

namespace WigiDash.DiscordWidget.Actions
{
    public interface IActionHandler : IDisposable
    {
        string ActionId { get; }
        string DisplayName { get; }
        string Description { get; }
        ActionAPIType APIType { get; }

        // Lifecycle
        Task InitializeAsync(ActionContext context);
        Task<Bitmap> RenderBitmapAsync(WidgetSize size);
        Task OnClickAsync(int x, int y);

        // Configuration
        UserControl GetConfigurationPanel();
        Dictionary<string, string> GetSettings();
        void LoadSettings(Dictionary<string, string> settings);

        // Template support
        List<TemplateVariable> GetAvailableVariables();

        // Prerequisites validation
        Task<PrerequisiteResult> ValidatePrerequisitesAsync();
    }

    public enum ActionAPIType
    {
        REST,        // Discord REST API
        RPC,         // Discord RPC Local
        Gateway,     // Discord Gateway (monitor only)
        Placeholder  // Not yet available
    }

    public class PrerequisiteResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }

        public static PrerequisiteResult Success() => new PrerequisiteResult { IsValid = true };
        public static PrerequisiteResult Failure(string message) => new PrerequisiteResult { IsValid = false, ErrorMessage = message };
    }

    public class TemplateVariable
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string ExampleValue { get; set; }
    }
}
