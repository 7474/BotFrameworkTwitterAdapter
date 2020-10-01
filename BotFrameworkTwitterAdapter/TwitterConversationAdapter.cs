using BotFrameworkTwitterAdapter.Services;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tweetinvi.Events;

namespace BotFrameworkTwitterAdapter
{
    public class TwitterConversationAdapter : BotAdapter
    {
        private readonly TwitterService twitterService;
        private readonly IBot bot;

        public TwitterConversationAdapter(TwitterService twitterService, IBot bot)
        {
            this.bot = bot;
            this.twitterService = twitterService;

            this.twitterService.TweetReceived += OnTweetReceivedAsync;
            this.twitterService.StartStream();
        }

        public new TwitterConversationAdapter Use(IMiddleware middleware)
        {
            MiddlewareSet.Use(middleware);
            return this;
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
                    long.Parse(activity.Recipient.Id),
                    activity.Recipient.Name);

                responses.Add(new ResourceResponse(activity.Id));
            }

            return responses.ToArray();
        }

        protected virtual async void OnTweetReceivedAsync(object sender, MatchedTweetReceivedEventArgs messageEventArgs)
        {
            TurnContext context = null;

            try
            {
                var activity = RequestToActivity(messageEventArgs);
                BotAssert.ActivityNotNull(activity);

                context = new TurnContext(this, activity);

                await RunPipelineAsync(context, bot.OnTurnAsync, default).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await OnTurnError(context, ex);
                throw;
            }
        }

        private Activity RequestToActivity(MatchedTweetReceivedEventArgs messageEventArgs)
        {
            var tweet = messageEventArgs.Tweet;
            var conversationId = tweet.IdStr;

            return new Activity
            {
                Text = tweet.Text,
                Type = "message",
                From = new ChannelAccount(tweet.CreatedBy.IdStr, tweet.CreatedBy.ScreenName),
                Recipient = new ChannelAccount(tweet.InReplyToUserIdStr, tweet.InReplyToScreenName),
                Conversation = new ConversationAccount { Id = conversationId },
                ChannelId = "twitter"
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
