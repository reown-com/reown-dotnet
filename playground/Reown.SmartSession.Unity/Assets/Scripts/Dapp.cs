using System;
using System.Collections.Generic;
using System.Numerics;
using Nethereum.ABI.EIP712;
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;
using Newtonsoft.Json;
using Reown.AppKit.Unity;
using Reown.AppKit.Unity.Http;
using Reown.AppKit.Unity.Profile;
using Reown.Core;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Sign.Nethereum.Model;
using UnityEngine;
using UnityEngine.UIElements;
using ButtonUtk = UnityEngine.UIElements.Button;

namespace Sample
{
    public class Dapp : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        private int _messageCounter = 0;

        private ButtonStruct[] _buttons;
        private VisualElement _buttonsContainer;

        private PermissionsResponse _permissionsResponse;

        private const string BackendUrl = "http://localhost:3015";

        private const string ABI =
            @"[{""type"":""constructor"",""inputs"":[{""name"":""initialValue"",""type"":""uint256"",""internalType"":""uint256""}],""stateMutability"":""nonpayable""},{""type"":""function"",""name"":""getCount"",""inputs"":[],""outputs"":[{""name"":"""",""type"":""uint256"",""internalType"":""uint256""}],""stateMutability"":""view""},{""type"":""function"",""name"":""increment"",""inputs"":[],""outputs"":[{""name"":"""",""type"":""uint256"",""internalType"":""uint256""}],""stateMutability"":""nonpayable""},{""type"":""event"",""name"":""CounterIncremented"",""inputs"":[{""name"":""caller"",""type"":""address"",""indexed"":true,""internalType"":""address""},{""name"":""newValue"",""type"":""uint256"",""indexed"":false,""internalType"":""uint256""}],""anonymous"":false}]";

        private void Awake()
        {
            Application.targetFrameRate = Screen.currentResolution.refreshRate;

            _buttonsContainer = _uiDocument.rootVisualElement.Q<VisualElement>("ButtonsContainer");

            BuildButtons();
        }

        private void BuildButtons()
        {
            _buttons = new[]
            {
                new ButtonStruct
                {
                    Text = "Connect",
                    OnClick = OnConnectButton,
                    AccountRequired = false
                },
                new ButtonStruct
                {
                    Text = "Network",
                    OnClick = OnNetworkButton
                },
                new ButtonStruct
                {
                    Text = "Account",
                    OnClick = OnAccountButton,
                    AccountRequired = true
                },
                new ButtonStruct
                {
                    Text = "Grant Permission",
                    OnClick = OnGrantPermissionButton,
                    AccountRequired = true
                },
                new ButtonStruct
                {
                    Text = "Personal Sign",
                    OnClick = OnPersonalSignButton,
                    AccountRequired = true
                },
                new ButtonStruct
                {
                    Text = "Increment",
                    OnClick = OnIncrementButton,
                    AccountRequired = true
                },
                new ButtonStruct
                {
                    Text = "Disconnect",
                    OnClick = OnDisconnectButton,
                    AccountRequired = true
                }
            };
        }

        private void RefreshButtons()
        {
            _buttonsContainer.Clear();

            foreach (var button in _buttons)
            {
                if (button.ChainIds != null && !button.ChainIds.Contains(AppKit.NetworkController?.ActiveChain?.ChainId))
                    continue;

                var buttonUtk = new ButtonUtk
                {
                    text = button.Text
                };
                buttonUtk.clicked += button.OnClick;

                if (button.AccountRequired.HasValue)
                {
                    switch (button.AccountRequired)
                    {
                        case true when !AppKit.IsAccountConnected:
                            buttonUtk.SetEnabled(false);
                            break;
                        case true when AppKit.IsAccountConnected:
                            buttonUtk.SetEnabled(true);
                            break;
                        case false when AppKit.IsAccountConnected:
                            buttonUtk.SetEnabled(false);
                            break;
                        case false when !AppKit.IsAccountConnected:
                            buttonUtk.SetEnabled(true);
                            break;
                    }
                }

                _buttonsContainer.Add(buttonUtk);
            }
        }

        private async void Start()
        {
            if (!AppKit.IsInitialized)
            {
                Notification.ShowMessage("AppKit is not initialized. Please initialize AppKit first.");
                return;
            }

            RefreshButtons();

            try
            {
                AppKit.ChainChanged += (_, e) =>
                {
                    RefreshButtons();

                    if (e.NewChain == null)
                    {
                        Notification.ShowMessage("Unsupported chain");
                        return;
                    }
                };

                AppKit.AccountConnected += (_, e) => RefreshButtons();

                AppKit.AccountDisconnected += (_, _) => RefreshButtons();

                AppKit.AccountChanged += (_, e) => RefreshButtons();

                AppKit.NetworkController.ChainChanged += (_, e) => RefreshButtons();

                // After the scene and UI are loaded, try to resume the session from the storage
                var sessionResumed = await AppKit.ConnectorController.TryResumeSessionAsync();
                Debug.Log($"Session resumed: {sessionResumed}");
            }
            catch (Exception e)
            {
                Notification.ShowMessage(e.Message);
                throw;
            }
        }

        public void OnConnectButton()
        {
            AppKit.OpenModal();
        }

        public void OnNetworkButton()
        {
            AppKit.OpenModal(ViewType.NetworkSearch);
        }

        public void OnAccountButton()
        {
            AppKit.OpenModal(ViewType.Account);
        }

        public async void OnGrantPermissionButton()
        {
            Debug.Log("[AppKit Sample] OnGrantPermissionButton");

            if (AppKit.NetworkController.ActiveChain.ChainReference != ChainConstants.References.BaseSepolia)
            {
                Notification.ShowMessage("Switch to Base Sepolia...");
                return;
            }

            try
            {
                var httpClient = new UnityHttpClient();

                Notification.ShowMessage("Requesting permissions...");

                Debug.Log($"$[AppKit Sample] Getting backend signer key at {BackendUrl}/signer");
                var response = await httpClient.GetAsync<Dictionary<string, string>>($"{BackendUrl}/signer");
                Debug.Log($"[AppKit Sample] Signer's key: {response["key"]}");

                var signer = new Signer(response["key"]);

                var permission = new Permission
                {
                    Type = "contract-call",
                    Data = new Dictionary<string, object>
                    {
                        {
                            "address", "0x0C46225550aCa909460150E341B49d447e3b2c75"
                        },
                        {
                            "abi", ABI
                        },
                        {
                            "functions", new Function[]
                            {
                                new()
                                {
                                    FunctionName = "increment"
                                }
                            }
                        }
                    }
                };

                Debug.Log($"[AppKit Sample] Granting permission:\n{JsonConvert.SerializeObject(permission, Formatting.Indented)}");

                var account = AppKit.Account;
                var chainId = "0x14a34";
                var address = account.Address;

                var permissionRequest = new PermissionsRequest(signer, TimeSpan.FromDays(10), chainId, address, permission);

                Debug.Log($"[AppKit Sample] Permission request:\n{JsonConvert.SerializeObject(permissionRequest, Formatting.Indented)}");

                var result = await AppKit.Evm.GrantPermissionsAsync(permissionRequest);

                Debug.Log($"[AppKit Sample] Permission response:\n{JsonConvert.SerializeObject(result, Formatting.Indented)}");

                Notification.ShowMessage($"Permission granted! Context: {result.Context}");

                _permissionsResponse = result;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public async void OnIncrementButton()
        {
            if (_permissionsResponse == null)
            {
                Notification.ShowMessage("No permissions found.");
                return;
            }

            try
            {
                Notification.ShowMessage("Requesting /increment");

                var httpClient = new UnityHttpClient();
                var permissionString = JsonConvert.SerializeObject(_permissionsResponse);
                var response = await httpClient.PostAsync<Dictionary<string, string>>($"{BackendUrl}/increment", permissionString);
                Debug.Log($"[AppKit Sample] Increment response:\n{JsonConvert.SerializeObject(response, Formatting.Indented)}");
                Notification.ShowMessage("Success (see logs)");
            }
            catch (Exception e)
            {
                Notification.ShowMessage($"Error processing increment request\n\n{nameof(RpcResponseException)}:\n{e.Message}");
                Debug.LogException(e, this);
            }
        }

        public async void OnPersonalSignButton()
        {
            Debug.Log("[AppKit Sample] OnPersonalSignButton");

            var messageCounter = ++_messageCounter;
            try
            {
                var account = await AppKit.GetAccountAsync();

                var message = $"Hello from Unity! (Request #{messageCounter})";

                Notification.ShowMessage($"Signing message:\n\n{message}");

#if !UNITY_WEBGL || !UNITY_EDITOR
                // await System.Threading.Tasks.Task.Delay(1_000);
#endif

                // It's also possible to sign a message as a byte array
                // var messageBytes = System.Text.Encoding.UTF8.GetBytes(message);
                // var signature = await AppKit.Evm.SignMessageAsync(messageBytes);

                var signature = await AppKit.Evm.SignMessageAsync(message);
                Debug.Log($"Recieved signature: {signature}");
                var isValid = await AppKit.Evm.VerifyMessageSignatureAsync(account.Address, message, signature);

                Notification.ShowMessage($"Signature valid: {isValid} (Request #{messageCounter})");
            }
            catch (ReownNetworkException e)
            {
                Notification.ShowMessage($"Error processing personal_sign request #{messageCounter}\n\n{nameof(RpcResponseException)}:\n{e.Message}");
                Debug.LogException(e, this);
            }
        }

        public async void OnDisconnectButton()
        {
            Debug.Log("[AppKit Sample] OnDisconnectButton");

            try
            {
                Notification.ShowMessage($"Disconnecting...");
                await AppKit.DisconnectAsync();
                Notification.Hide();
            }
            catch (Exception e)
            {
                Notification.ShowMessage($"{e.GetType()}:\n{e.Message}");
                Debug.LogException(e, this);
            }
        }
    }

    internal struct ButtonStruct
    {
        public string Text;
        public Action OnClick;
        public bool? AccountRequired;
        public HashSet<string> ChainIds;
    }
}