using BotFrameworkTwitterAdapter.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using Tweetinvi.Models;
using Tweetinvi.Models.DTO;
using Tweetinvi.Parameters;

namespace BotFrameworkTwitterAdapter.Services
{
    public class TwitterService : IDisposable
    {
        /// <summary>
        /// Fired when a @ tweet is received.
        /// </summary>
        public event EventHandler<Tweetinvi.Events.MatchedTweetReceivedEventArgs> TweetReceived;

        private IUser _botUser;
        private Tweetinvi.Streaming.IFilteredStream _filteredStream;
        private BackgroundWorker streamWorker;

        private readonly ILogger<TwitterService> logger;

        public TwitterService(IOptions<TwitterServiceOptions> options, ILoggerFactory loggerFactory)
        {
            logger = loggerFactory.CreateLogger<TwitterService>();
            var option = options.Value;
            if (string.IsNullOrEmpty(option.ConsumerKey) || string.IsNullOrEmpty(option.ConsumerSecret))
            {
                throw new ArgumentNullException("Both consumer key and secret must be valid");
            }
            if (string.IsNullOrEmpty(option.AccessToken) || string.IsNullOrEmpty(option.AccessTokenSecret))
            {
                if (string.IsNullOrEmpty(option.BearerToken))
                {
                    Tweetinvi.Auth.SetApplicationOnlyCredentials(option.ConsumerKey, option.ConsumerSecret);
                }
                else
                {
                    Tweetinvi.Auth.SetApplicationOnlyCredentials(option.ConsumerKey, option.ConsumerSecret, option.BearerToken);
                }
            }
            else
            {
                Tweetinvi.Auth.SetUserCredentials(option.ConsumerKey, option.ConsumerSecret, option.AccessToken, option.AccessTokenSecret);
            }
            _botUser = Tweetinvi.User.GetAuthenticatedUser();
            streamWorker = new BackgroundWorker
            {
                WorkerSupportsCancellation = true,
            };
            streamWorker.DoWork += ProcessStream;
        }

        private async void ProcessStream(object sender, DoWorkEventArgs e)
        {
            var worker = sender as BackgroundWorker;
            while (!worker.CancellationPending)
            {
                try
                {
                    await _filteredStream.StartStreamMatchingAllConditionsAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error Twitter converstain stream. " + ex.Message);
                }
            }
        }

        public void Dispose()
        {
            if (_filteredStream != null)
            {
                streamWorker.CancelAsync();
                _filteredStream.MatchingTweetReceived -= OnMatchingTweetReceived;
                _filteredStream.StopStream();
                _filteredStream = null;
            }
        }

        public void StartStream()
        {
            if (_filteredStream == null)
            {
                // For Reply
                _filteredStream = Tweetinvi.Stream.CreateFilteredStream();
                _filteredStream.AddTrack("@" + _botUser.ScreenName);
                _filteredStream.MatchingTweetReceived += OnMatchingTweetReceived;
                streamWorker.RunWorkerAsync();
                logger.LogInformation("Twitter stream start");
            }
            else
            {
                logger.LogWarning("Twitter stream already started");
            }
        }

        private void OnMatchingTweetReceived(object sender, Tweetinvi.Events.MatchedTweetReceivedEventArgs e)
        {
            logger.LogInformation($"OnMatchingTweetReceived Twitter message received. {JsonConvert.SerializeObject(e)}");
            if (e.Tweet.CreatedBy.Id == _botUser.Id)
            {
                logger.LogInformation("Skip. Tweet is created by bot.");
                return;
            }
            TweetReceived?.Invoke(this, e);
        }

        public ITweet SendReply(
            string messageText,
            // TODO そのうち渡し方を見直す
            IList<string> mediaUrls,
            long replyToId,
            params string[] toScreanNames)
        {
            logger.LogInformation($"SendReply. {replyToId} {messageText}");
            var replyTo = new TweetIdentifier(replyToId);
            var atNames = string.Join(" ",
                toScreanNames
                    .Where(x => !string.IsNullOrEmpty(x))
                    .Where(x => x != _botUser.ScreenName)
                    .Distinct()
                    .Select(x => "@" + x));

            // 動画なら1件だけ、他は画像とみなして4件まで
            var videoMediaUrls = mediaUrls
                .Where(x => x.ToLower().EndsWith("gif") || x.ToLower().EndsWith("api"));
            var attachMediaUrls = videoMediaUrls.Any()
                ? videoMediaUrls.Take(1)
                : mediaUrls.Take(4);
            var mediaBinaries = attachMediaUrls
                .Select(x => new BinaryReader(
                    WebRequest.Create(x).GetResponse().GetResponseStream()
                ).ReadAllBytes()).ToList();
            // TODO メッセージをURLなどを考慮した長さに正規化する
            // TODO 添付の仕方を見直す（ビデオ対応など。。。）
            // https://github.com/linvi/tweetinvi/issues/53
            return Tweetinvi.Tweet.PublishTweet(
                $"{atNames} {messageText}".SafeSubstring(0, 140),
                new PublishTweetOptionalParameters
                {
                    InReplyToTweet = replyTo,
                    MediaBinaries = mediaBinaries,
                });
        }

        public bool IsSendToBot(ITweetDTO tweet)
        {
            return tweet.InReplyToUserId.HasValue
                && tweet.InReplyToUserId.Value == _botUser.Id;
        }
    }
}
