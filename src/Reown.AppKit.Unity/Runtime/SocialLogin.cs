using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// ReSharper disable ArrangeAccessorOwnerBody

namespace Reown.AppKit.Unity
{
    public class SocialLogin
    {
        public string Name { get; private set; }
        public string Slug { get; private set; }

        public SocialLogin(string name, string slug)
        {
            Name = name;
            Slug = slug;
        }

        /// <summary>
        ///    Opens the social login provider in AppKit modal.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the AppKit is not initialized</exception>
        public void Open()
        {
            if (!AppKit.IsInitialized)
                throw new InvalidOperationException("AppKit is not initialized");

            PlayerPrefs.SetString("RE_SOCIAL_PROVIDER_NAME", Slug);
            AppKit.OpenModal(ViewType.WebWallet);
        }

        /// <summary>
        ///     Initiates a connection to the social login provider and waits asynchronously until the user successfully connects or the operation is cancelled.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
        /// <exception cref="InvalidOperationException">Thrown when the AppKit is not initialized or the account is already connected</exception>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (!AppKit.IsInitialized)
                throw new InvalidOperationException("AppKit is not initialized");

            if (AppKit.IsAccountConnected)
                throw new InvalidOperationException("Account is already connected");

            var tcs = new TaskCompletionSource<bool>();

            await using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    AppKit.AccountConnected -= AccountConnectedHandler;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to unsubscribe from AccountConnected event: {ex.Message}");
                    // Continue execution since this is cleanup code
                }

                tcs.TrySetCanceled();
            });

            AppKit.AccountConnected += AccountConnectedHandler;

            void AccountConnectedHandler(object _, Connector.AccountConnectedEventArgs e)
            {
                AppKit.AccountConnected -= AccountConnectedHandler;
                tcs.TrySetResult(true);
            }

            Open();

            await tcs.Task;
        }

        public static SocialLogin Google => new("Google", "google");
        public static SocialLogin Apple => new("Apple", "apple");
        public static SocialLogin X => new("X", "x");
        public static SocialLogin GitHub => new("GitHub", "github");
        public static SocialLogin Facebook => new("Facebook", "facebook");
        public static SocialLogin Discord => new("Discord", "discord");
    }
}