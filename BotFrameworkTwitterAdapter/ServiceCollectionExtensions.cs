using BotFrameworkTwitterAdapter.Services;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace BotFrameworkTwitterAdapter
{
    public static class ServiceCollectionExtensions
    {
        public static void AddTwitterConversationAdapter(
            this IServiceCollection collection,
            Action<TwitterServiceOptions> serviceContextDelegate,
            Action<TwitterConversationAdapterOptions> adapterContextDelegate
        )
        {
            collection.AddSingleton<TwitterService>();
            collection.AddSingleton<TwitterConversationAdapter>();

            collection.AddOptions();
            collection.Configure(serviceContextDelegate);
            collection.Configure(adapterContextDelegate);
        }
    }
}
