using System;
using System.Collections.Generic;
using System.Numerics;
using Nethereum.ABI.EIP712;
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;
using Newtonsoft.Json;
using Reown.AppKit.Unity;
using Reown.AppKit.Unity.Profile;
using Reown.Core;
using Reown.Core.Common.Model.Errors;
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
                    Text = "Personal Sign",
                    OnClick = OnPersonalSignButton,
                    AccountRequired = true
                },
                // new ButtonStruct
                // {
                //     Text = "Sign Typed Data",
                //     OnClick = OnSignTypedDataV4Button,
                //     AccountRequired = true
                // },
                new ButtonStruct
                {
                    Text = "Send Transaction",
                    OnClick = OnSendTransactionButton,
                    AccountRequired = true
                },
                new ButtonStruct
                {
                    Text = "Get Balance",
                    OnClick = OnGetBalanceButton,
                    AccountRequired = true
                },
                new ButtonStruct
                {
                    Text = "Read Contract",
                    OnClick = OnReadContractClicked,
                    // AccountRequired = true,
                    ChainIds = new HashSet<string>
                    {
                        "eip155:10"
                    }
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

        public async void OnConnectButton()
        {
            // await AppKit.ConnectAsync("26d3d9e7224a1eb49089aa5f03fb9f3b883e04050404594d980d4e1e74e1dbea"); // abs
            await AppKit.ConnectAsync("53332571a2e1b748add766da51b90dc9d1e9d65d2c301969add5ba3939339513");
            // AppKit.OpenModal();
        }

        public void OnNetworkButton()
        {
            AppKit.OpenModal(ViewType.NetworkSearch);
        }

        public void OnAccountButton()
        {
            AppKit.OpenModal(ViewType.Account);
        }

        public async void OnGetBalanceButton()
        {
            Debug.Log("[AppKit Sample] OnGetBalanceButton");

            try
            {
                Notification.ShowMessage("Getting balance with WalletConnect Blockchain API...");

                var account = AppKit.Account;

                var balance = await AppKit.Evm.GetBalanceAsync(account.Address);

                Notification.ShowMessage($"Balance: {Web3.Convert.FromWei(balance)} ETH");
            }
            catch (Exception e)
            {
                Notification.ShowMessage($"{nameof(RpcResponseException)}:\n{e.Message}");
                Debug.LogException(e, this);
            }
        }

        public async void OnPersonalSignButton()
        {
            Debug.Log("[AppKit Sample] OnPersonalSignButton");

            var messageCounter = ++_messageCounter;
            try
            {
                var account = AppKit.Account;

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

        public async void OnSendTransactionButton()
        {
            Debug.Log("[AppKit Sample] OnSendTransactionButton");

            const string toAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";

            try
            {
                Notification.ShowMessage("Sending transaction...");

                var value = Web3.Convert.ToWei(0.001);
                var result = await AppKit.Evm.SendTransactionAsync(toAddress, value);
                Debug.Log("Transaction hash: " + result);

                Notification.ShowMessage("Transaction sent");
            }
            catch (Exception e)
            {
                Notification.ShowMessage($"Error sending transaction.\n{e.Message}");
                Debug.LogException(e, this);
            }
        }

        public async void OnSignTypedDataV4Button()
        {
            Debug.Log("[AppKit Sample] OnSignTypedDataV4Button");

            Notification.ShowMessage("Signing typed data...");

            var account = AppKit.Account;

            Debug.Log("Get mail typed definition");
            var typedData = GetMailTypedDefinition();
            var mail = new Mail
            {
                From = new Person
                {
                    Name = "Cow",
                    Wallets = new List<string>
                    {
                        "0xCD2a3d9F938E13CD947Ec05AbC7FE734Df8DD826",
                        "0xDeaDbeefdEAdbeefdEadbEEFdeadbeEFdEaDbeeF"
                    }
                },
                To = new List<Person>
                {
                    new()
                    {
                        Name = "Bob",
                        Wallets = new List<string>
                        {
                            "0xbBbBBBBbbBBBbbbBbbBbbbbBBbBbbbbBbBbbBBbB",
                            "0xB0BdaBea57B0BDABeA57b0bdABEA57b0BDabEa57",
                            "0xB0B0b0b0b0b0B000000000000000000000000000"
                        }
                    }
                },
                Contents = "Hello, Bob!"
            };

            // Convert CAIP-2 chain reference to EIP-155 chain ID
            // This is equivalent to `account.ChainId.Split(":")[1]`, but allocates less memory
            var ethChainId = Utils.ExtractChainReference(account.ChainId);

            typedData.Domain.ChainId = BigInteger.Parse(ethChainId);
            typedData.SetMessage(mail);

            var jsonMessage = typedData.ToJson();

            try
            {
                var signature = await AppKit.Evm.SignTypedDataAsync(jsonMessage);

                var isValid = await AppKit.Evm.VerifyTypedDataSignatureAsync(account.Address, jsonMessage, signature);

                Notification.ShowMessage($"Signature valid: {isValid}");
            }
            catch (Exception e)
            {
                Notification.ShowMessage("Error signing typed data");
                Debug.LogException(e, this);
            }
        }

        public async void OnReadContractClicked()
        {
            // An example of reading a smart contract state.
            // This example uses the WCT staking contract on the Optimism Mainnet

            const string contractAddress = "0x521B4C065Bbdbe3E20B3727340730936912DfA46";

            // Can be JSON or human-readable ABI that includes only the function you want to call.
            // It's recommended to use JSON ABI for better performance.
            const string abi = "function supply() view returns (uint256)";

            Notification.ShowMessage("Reading smart contract state...");

            try
            {
                var staked = await AppKit.Evm.ReadContractAsync<BigInteger>(contractAddress, abi, "supply");
                var stakedFormated = Web3.Convert.FromWei(staked); // WCT token has 18 decimals
                var result = $"Total Tokens Staked:\n{stakedFormated:N0} WCT";

                Notification.ShowMessage(result);
            }
            catch (Exception e)
            {
                Notification.ShowMessage($"Contract reading error.\n{e.Message}");
                Debug.LogException(e, this);
            }
        }

        private TypedData<Domain> GetMailTypedDefinition()
        {
            return new TypedData<Domain>
            {
                Domain = new Domain
                {
                    Name = "Ether Mail",
                    Version = "1",
                    ChainId = 1,
                    VerifyingContract = "0xCcCCccccCCCCcCCCCCCcCcCccCcCCCcCcccccccC"
                },
                Types = MemberDescriptionFactory.GetTypesMemberDescription(typeof(Domain), typeof(Group), typeof(Mail), typeof(Person)),
                PrimaryType = nameof(Mail)
            };
        }

        public const string CryptoPunksAbi =
            @"[{""constant"":true,""inputs"":[{""name"":""_owner"",""type"":""address""}],""name"":""balanceOf"",""outputs"":[{""name"":""balance"",""type"":""uint256""}],""payable"":false,""stateMutability"":""view"",""type"":""function""},
        {""constant"":true,""inputs"":[],""name"":""name"",""outputs"":[{""name"":"""",""type"":""string""}],""payable"":false,""stateMutability"":""view"",""type"":""function""}]";
    }

    internal struct ButtonStruct
    {
        public string Text;
        public Action OnClick;
        public bool? AccountRequired;
        public HashSet<string> ChainIds;
    }
}