using System;
using System.Collections.Generic;
using System.Numerics;
using Nethereum.ABI.EIP712;
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;
using Reown.AppKit.Unity;
using UnityEngine;
using UnityEngine.UIElements;
using ButtonUtk = UnityEngine.UIElements.Button;

namespace Sample
{
    public class Dapp : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

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
                new ButtonStruct
                {
                    Text = "Sign Typed Data",
                    OnClick = OnSignTypedDataV4Button,
                    AccountRequired = true
                },
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
                    AccountRequired = true,
                    ChainIds = new HashSet<string>
                    {
                        "eip155:1"
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

                    if (e.Chain == null)
                    {
                        Notification.ShowMessage("Unsupported chain");
                        return;
                    }
                };

                AppKit.AccountConnected += async (_, e) => { RefreshButtons(); };

                AppKit.AccountDisconnected += (_, _) => { RefreshButtons(); };

                AppKit.AccountChanged += (_, e) => { RefreshButtons(); };

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

        public async void OnGetBalanceButton()
        {
            Debug.Log("[AppKit Sample] OnGetBalanceButton");

            try
            {
                Notification.ShowMessage("Getting balance with WalletConnect Blockchain API...");

                var account = await AppKit.GetAccountAsync();

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

            try
            {
                var account = await AppKit.GetAccountAsync();

                const string message = "Hello from Unity!";
                var signature = await AppKit.Evm.SignMessageAsync(message);
                var isValid = await AppKit.Evm.VerifyMessageSignatureAsync(account.Address, message, signature);

                Notification.ShowMessage($"Signature valid: {isValid}");
            }
            catch (RpcResponseException e)
            {
                Notification.ShowMessage($"{nameof(RpcResponseException)}:\n{e.Message}");
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

            var account = await AppKit.GetAccountAsync();

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

            typedData.Domain.ChainId = BigInteger.Parse(account.ChainId.Split(":")[1]);
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
            if (AppKit.NetworkController.ActiveChain.ChainId != "eip155:1")
            {
                Notification.ShowMessage("Please switch to Ethereum mainnet.");
                return;
            }

            const string contractAddress = "0xb47e3cd837ddf8e4c57f05d70ab865de6e193bbb"; // on Ethereum mainnet
            const string yugaLabsAddress = "0xA858DDc0445d8131daC4d1DE01f834ffcbA52Ef1";
            const string abi = CryptoPunksAbi;

            Notification.ShowMessage("Reading smart contract state...");

            try
            {
                var tokenName = await AppKit.Evm.ReadContractAsync<string>(contractAddress, abi, "name");
                Debug.Log($"Token name: {tokenName}");

                var balance = await AppKit.Evm.ReadContractAsync<BigInteger>(contractAddress, abi, "balanceOf", new object[]
                {
                    yugaLabsAddress
                });
                var result = $"Yuga Labs owns: {balance} {tokenName} tokens active chain.";

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