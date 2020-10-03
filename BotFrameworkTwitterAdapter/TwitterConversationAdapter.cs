using BotFrameworkTwitterAdapter.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Tweetinvi.Events;
using Tweetinvi.Logic.DTO;
using Tweetinvi.Models.DTO;

namespace BotFrameworkTwitterAdapter
{
    public class TwitterConversationAdapter : BotAdapter
    {
        private readonly TwitterService twitterService;
        private readonly TwitterConversationAdapterOptions option;
        private readonly HttpClient botTwitterApiClient;

        public TwitterConversationAdapter(
            TwitterService twitterService,
            IOptions<TwitterConversationAdapterOptions> options
        )
        {
            this.option = options.Value;
            this.twitterService = twitterService;
            botTwitterApiClient = new HttpClient
            {
                BaseAddress = new Uri(option.BotTwitterApiEndpoint),
            };

            this.twitterService.TweetReceived += OnTweetReceivedAsync;
        }

        public new TwitterConversationAdapter Use(Microsoft.Bot.Builder.IMiddleware middleware)
        {
            MiddlewareSet.Use(middleware);
            return this;
        }

        public void Start()
        {
            twitterService.StartStream();
        }

        public override async Task<ResourceResponse[]> SendActivitiesAsync(ITurnContext turnContext,
            Activity[] activities, CancellationToken cancellationToken)
        {
            var responses = new List<ResourceResponse>();
            foreach (var activity in activities.Where(a => a.Type == ActivityTypes.Message))
            {
                var mediaUrls = activity.Attachments.Select(x => x.ContentUrl).ToList();

                var replyTweet = twitterService.SendReply(
                    activity.Text,
                    mediaUrls,
                    long.Parse(activity.Conversation.Id),
                    activity.Recipient.Name);

                responses.Add(new ResourceResponse(activity.Id));
            }

            return responses.ToArray();
        }

        private async void OnTweetReceivedAsync(object sender, MatchedTweetReceivedEventArgs messageEventArgs)
        {
            // ProcessAsync へバイパスするリクエストを行う。
            var res = await botTwitterApiClient.PostAsync("",
                new StringContent(JsonConvert.SerializeObject(messageEventArgs.Tweet.TweetDTO)));
            if ((int)res.StatusCode >= 300)
            {
                throw new IOException($"Tweet process request failed. {res.StatusCode}");
            }
        }

        public async Task ProcessAsync(HttpRequest httpRequest, HttpResponse httpResponse, IBot bot, CancellationToken cancellationToken = default)
        {
            if (httpRequest == null)
            {
                throw new ArgumentNullException(nameof(httpRequest));
            }

            if (httpResponse == null)
            {
                throw new ArgumentNullException(nameof(httpResponse));
            }

            if (bot == null)
            {
                throw new ArgumentNullException(nameof(bot));
            }

            // TODO validate
            // 当面変なリクエストを送られても問題はない気はする
            // OnTweetReceivedAsync でAPIKey的なモノをヘッダに指定して参考コードと同じ検証をしてやればいい。
            //if (_options.ValidateIncomingZoomRequests &&
            //    httpRequest.Headers.TryGetValue("HeaderAuthorization", out StringValues headerAuthorization)
            //    && headerAuthorization.FirstOrDefault() != _options.VerificationToken)
            //{
            //    throw new AuthenticationException("Failed to validate incoming request. Mismatched verification token.");
            //}

            string body;
            using (var sr = new StreamReader(httpRequest.Body))
            {
                body = await sr.ReadToEndAsync();
            }

            // https://github.com/linvi/tweetinvi/wiki/Serialization-and-Deserialization
            var tweetRequest = Tweetinvi.JsonSerializer.ConvertJsonTo<ITweetDTO>(body);

            if (!twitterService.IsSendToBot(tweetRequest))
            {
                httpResponse.StatusCode = 400;
                return;
            }

            var activity = RequestToActivity(tweetRequest);

            using (var context = new TurnContext(this, activity))
            {
                await RunPipelineAsync(context, bot.OnTurnAsync, cancellationToken).ConfigureAwait(false);
            }
            httpResponse.StatusCode = 204;
        }

        private Activity RequestToActivity(ITweetDTO tweet)
        {
            var conversationId = tweet.IdStr;

            return new Activity
            {
                Text = tweet.Text,
                Type = "message",
                From = new ChannelAccount(tweet.CreatedBy.IdStr, tweet.CreatedBy.ScreenName),
                Recipient = new ChannelAccount(tweet.InReplyToUserIdStr, tweet.InReplyToScreenName),
                Conversation = new ConversationAccount { Id = conversationId },
                ChannelId = "twitter_conversation",
                // Fix TrustServiceUrl
                // https://github.com/microsoft/botbuilder-dotnet/blob/3a0dcfee003a7fcd0271c6336ab2c166f9a74313/libraries/Microsoft.Bot.Builder/BotFrameworkAdapter.cs#L368
                // XXX どういう理由で ServiceUrl が求められているのか分かっていない
                //ServiceUrl = option.BotTwitterApiEndpoint,
                // XXX 会話継続時のActivity送信先っぽいのでDirectlineを指定しておけばいい？
                ServiceUrl = "https://directline.botframework.com/",
            };
        }

        public override Task<ResourceResponse> UpdateActivityAsync(ITurnContext turnContext, Activity activity, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public override Task DeleteActivityAsync(ITurnContext turnContext, ConversationReference reference,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
