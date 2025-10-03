using UnityEngine;
using UnityEngine.UI;
using Reown.AppKit.Unity;
using Reown.AppKit.Unity.Model;
using System.Threading.Tasks;
using TMPro;

public class WalletController : MonoBehaviour
{
    [Header("UI Dependencies")]
    [SerializeField] private Button _initButton;
    [SerializeField] private Button _networkButton;
    [SerializeField] private Button _connectButton;
    [SerializeField] private Button _disconnectButton;
    [SerializeField] private Button _accountButton;
    [SerializeField] private TMP_Text _walletAddressText;
    [SerializeField] private TMP_Text _walletBalanceText;

    [Header("Project Information")]
    [SerializeField] private string _projectId;
    [SerializeField] private string _projectName;
    [SerializeField] private string _projectDescription;
    [SerializeField] private string _projectUrl;
    [SerializeField] private string _projectIconUrl;

    private void Awake(){
        _initButton.interactable = true;
        _networkButton.interactable = false;
        _connectButton.interactable = false;
        _disconnectButton.interactable = false;
        _accountButton.interactable = false;
    }

    public void Init(){
        Debug.Log("tomicz: Started initializing AppKit");
        SetAppKitConfig();
    }

    private async void SetAppKitConfig(){
        AppKitConfig config = new AppKitConfig(
            _projectId = _projectId,
            new Metadata(
                _projectName,
                _projectDescription,
                _projectUrl,
                _projectIconUrl,
                new RedirectData
                {
                    Native = "tomicz-sample-unity://"
                }
            )
        );

        if (config != null) {
            Debug.Log("tomicz: AppKit config is null");
        } else {
            Debug.Log("tomicz: AppKit config is not null");
        }

        Debug.Log("tomicz: AppKit initialized");
        Debug.Log("tomicz: Project ID: " + _projectId);
        Debug.Log("tomicz: Project Name: " + _projectName);
        Debug.Log("tomicz: Project Description: " + _projectDescription);
        Debug.Log("tomicz: Project URL: " + _projectUrl);
        Debug.Log("Project Icon URL: " + _projectIconUrl);

        Debug.Log("tomicz: Initializing AppKit");
        await AppKit.InitializeAsync(config);

        if(AppKit.IsInitialized) {
            Debug.Log("tomicz: AppKit initialized");
            _connectButton.interactable = true;
            AppKit.AccountConnected += OnAccountConnected;
            _initButton.interactable = false;
        } else {
            Debug.Log("tomicz: AppKit not initialized");
        }
    }

    public void SelectNetwork(){
        if(AppKit.IsAccountConnected){
            AppKit.OpenModal(ViewType.NetworkSearch);
            Debug.Log("tomicz: Network modal opened");
        } else {
            Debug.LogError("tomicz: Account not connected");
        }
    }

    public void Connect(){
        AppKit.OpenModal(ViewType.Connect);
        Debug.Log("tomicz: Modal opened");
    }

    public void Disconnect(){
        if(AppKit.IsAccountConnected){
            AppKit.DisconnectAsync();
            Debug.Log("tomicz: Account disconnected");
            _disconnectButton.interactable = false;
            _connectButton.interactable = true;
            _accountButton.interactable = false;
            _walletAddressText.text = "Address: 0x00";
            _networkButton.interactable = false;
        }
    }

    public void GetAccount(){
        var account = AppKit.Account;
        Debug.Log("tomicz: Account: " + account);
        _walletAddressText.text = account.Address;
    }

    private async void OnAccountConnected(object sender, Connector.AccountConnectedEventArgs e){
        Debug.Log("tomicz: Account connected successfully");
        _disconnectButton.interactable = true;
        _connectButton.interactable = false;
        _accountButton.interactable = true;
        _walletAddressText.text = e.Account.Address;
        Debug.Log("tomicz: Set wallet address: " + e.Account.Address);
        _networkButton.interactable = true;
        Debug.Log("tomicz: Network button interactable");

        Debug.Log("tomicz: Updating wallet balance");
        await AppKit.AccountController.UpdateBalance();
        Debug.Log("tomicz: Wallet balance updated");
        float balance = AppKit.AccountController.NativeTokenBalance;
        string symbol = AppKit.AccountController.NativeTokenSymbol;
        _walletBalanceText.text = $"{balance} - {symbol}";
        Debug.Log("tomicz: Set wallet balance: " + balance);
        Debug.Log("tomicz: Wallet Native Token Symbol: " + symbol);
    }
}