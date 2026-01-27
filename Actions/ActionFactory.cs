using System;

namespace WigiDash.DiscordWidget.Actions
{
    public static class ActionFactory
    {
        public static IActionHandler CreateAction(string actionType, ActionContext context)
        {
            switch (actionType?.ToLowerInvariant())
            {
                case "voice_channel":
                    var action = new VoiceChannelAction();
                    action.SetContext(context);
                    return action;

                case "soundboard":
                    // TODO: Implement SoundboardAction
                    return null;

                case "mute_toggle":
                    // TODO: Implement MuteToggleAction
                    return null;

                case "deafen_toggle":
                    // TODO: Implement DeafenToggleAction
                    return null;

                case "audio_device":
                    // TODO: Implement AudioDeviceAction
                    return null;

                case "server_stats":
                    // TODO: Implement ServerStatsAction
                    return null;

                case "video_toggle":
                    // TODO: Implement VideoToggleAction (placeholder)
                    return null;

                case "stream_toggle":
                    // TODO: Implement StreamToggleAction (placeholder)
                    return null;

                default:
                    return null;
            }
        }
    }
}
