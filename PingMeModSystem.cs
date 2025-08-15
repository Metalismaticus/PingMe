using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PingMe
{
    public class PingMeModSystem : ModSystem
    {
        ICoreClientAPI capi;
        ToastManager toastManager;
        PingmeService pingme;
        PingmeConfig config;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;

            config = capi.LoadModConfig<PingmeConfig>("pingme.json") ?? new PingmeConfig();
            toastManager = new ToastManager(capi, config);
            pingme = new PingmeService(capi, toastManager, config);

            capi.Event.ChatMessage += pingme.OnChatMessage;
            capi.Event.OnSendChatMessage += pingme.OnSendChatMessage;
        }

        public override void Dispose()
        {
            if (capi == null) return;
            if (pingme != null)
            {
                capi.Event.ChatMessage -= pingme.OnChatMessage;
                capi.Event.OnSendChatMessage -= pingme.OnSendChatMessage;
            }
            capi.StoreModConfig(config, "pingme.json");
        }
    }
}