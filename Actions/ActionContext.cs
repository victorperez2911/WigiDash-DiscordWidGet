using WigiDashWidgetFramework;
using WigiDash.DiscordWidget.Core;
using WigiDash.DiscordWidget.Rendering;

namespace WigiDash.DiscordWidget.Actions
{
    public class ActionContext
    {
        public DiscordGatewayManager GatewayManager { get; set; }
        public DiscordRPCManager RPCManager { get; set; }
        public DiscordRESTClient RESTClient { get; set; }
        public BitmapRenderer Renderer { get; set; }
        public ImageCache ImageCache { get; set; }
        public IWidgetManager WidgetManager { get; set; }
    }
}
