mergeInto(LibraryManager.library, {
    // Global variable to store the loaded modules and configuration
    _appKitConfig: null,

    $SerializeJson: function (obj) {
        let cache = [];
        let resultJson = JSON.stringify(obj, (key, value) => {
            // Handle circular references
            if (typeof value === 'object' && value !== null) {
                if (cache.includes(value)) return;
                cache.push(value);
            }
            // Check if the value is a BigInt and convert it to a string
            if (typeof value === 'bigint') {
                return value.toString();
            }
            return value;
        });
        cache = null;
        return resultJson;
    },

    $ExecuteCallAsync__deps: ['$SerializeJson'],
    $ExecuteCallAsync: async function (callFn, id, methodNameStrPtr, parameterStrPtr, callbackPtr) {
        if (!_appKitConfig) {
            console.error("AppKit is not initialized. Call Initialize first.");
            return;
        }

        // Convert the method name and parameter to JS strings
        let methodName = UTF8ToString(methodNameStrPtr);
        let parameterStr = UTF8ToString(parameterStrPtr);
        
        let parameterObj = parameterStr === "" ? undefined : JSON.parse(parameterStr);

        try {
            // Call the method using the provided function
            let result = await callFn(_appKitConfig, methodName, parameterObj);
            
            if (result === undefined || result === null) {
                {{{makeDynCall('viii', 'callbackPtr')}}}(id, undefined, undefined);
                return;
            }

            let resultJson = SerializeJson(result);
            
            // Call the callback with the result
            let resultStrPtr = stringToNewUTF8(resultJson);
            {{{makeDynCall('viii', 'callbackPtr')}}}(id, resultStrPtr, undefined);
            _free(resultStrPtr);
        } catch (error) {
            console.error("[AppKit] Error executing call", error);
            let errorJson = JSON.stringify(error, ['name', 'message']);
            let errorStrPtr = stringToNewUTF8(errorJson);
            {{{makeDynCall('viii', 'callbackPtr')}}}(id, undefined, errorStrPtr);
            _free(errorStrPtr);
        }
    },

    $ExecuteCall__deps: ['$SerializeJson'],
    $ExecuteCall: function (callFn, methodNameStrPtr, parameterStrPtr) {
        if (!_appKitConfig) {
            console.error("AppKit is not initialized. Call Initialize first.");
            return;
        }

        // Convert the method name and parameter to JS strings
        let methodName = UTF8ToString(methodNameStrPtr);
        let parameterStr = UTF8ToString(parameterStrPtr);
        
        let parameterObj = parameterStr === "" ? undefined : JSON.parse(parameterStr);

        try {
            // Call the method using the provided function (synchronously)
            let result = callFn(_appKitConfig, methodName, parameterObj);

            if (result === undefined || result === null) {
                return null;
            }

            let resultJson = SerializeJson(result);
            
            let resultStrPtr = stringToNewUTF8(resultJson);
            return resultStrPtr;
        } catch (error) {
            console.error("[AppKit] Error executing sync call", error);
            let errorJson = JSON.stringify(error, ['name', 'message']);
            let errorStrPtr = stringToNewUTF8(errorJson);
            return errorStrPtr;
        }
    },

    // Preload the scripts from CDN, initialize the configuration and create the modal
    Initialize: function (parametersJsonPtr, callbackPtr) {
        const parametersJson = UTF8ToString(parametersJsonPtr);
        const parameters = JSON.parse(parametersJson);

        const projectId = parameters.projectId;
        const metadata = parameters.metadata;
        const chains = parameters.supportedChains;
        
        const enableEmail = parameters.enableEmail;
        const enableOnramp = parameters.enableOnramp;
        const enableAnalytics = parameters.enableAnalytics;
        const socials = parameters.socials;
        
        const excludeWalletIds = parameters.excludeWalletIds;
        const includeWalletIds = parameters.includeWalletIds;

        // Load the scripts and initialize the configuration
        import("https://cdn.jsdelivr.net/npm/@reown/appkit-cdn@1.7.3/dist/appkit.js").then(async (AppKit) => {
            const WagmiCore = AppKit['WagmiCore'];
            const WagmiAdapter = AppKit['WagmiAdapter'];
            const Viem = AppKit['Viem'];
            const Chains = AppKit['networks'];
            const reconnect = WagmiCore['reconnect'];
            const createAppKit = AppKit['createAppKit'];

            const networks = chains.map(c => Chains.defineChain(c));
            
            const wagmiAdapter = new WagmiAdapter({
                networks: networks,
                projectId
            })

            const modal = createAppKit({
                adapters: [wagmiAdapter],
                networks: networks,
                metadata: metadata,
                projectId,
                isUnity: true,
                excludeWalletIds: excludeWalletIds,
                includeWalletIds: includeWalletIds,
                features: {
                    email: enableEmail,
                    analytics: enableAnalytics,
                    onramp: enableOnramp,
                    socials: socials
                }
            })
            
            // Reconnect to WalletConnect when connector is ready
            WagmiCore.watchConnectors(wagmiAdapter.wagmiConfig, {
              onChange(connectors) {
                if (connectors.some(connector => connector.id === 'walletConnect')) {
                  reconnect(wagmiAdapter.wagmiConfig)
                }
              }
            })

            // Store the configuration and modal globally
            _appKitConfig = {
                config: wagmiAdapter.wagmiConfig,
                modal: modal,
                wagmiCore: WagmiCore,
                viem: Viem,
            };

            // Insert the container into the DOM at the canvas's original position
            const canvas = document.getElementsByTagName('canvas')[0];
            const container = document.createElement('div');
            container.id = 'canvas-container';
            canvas.parentNode.insertBefore(container, canvas);
            container.appendChild(canvas);

            const appkit = document.createElement('w3m-modal')
            container.appendChild(appkit)

            // Add styles to enable fullscreen compatibility
            const addCanvasActiveStyles = () => {
                const styleElement = document.createElement('style');
                styleElement.id = 'canvas-active-styles';
                styleElement.innerHTML = `
                .canvas-active {
                    position: fixed !important;
                    top: 0 !important;
                    right: 0 !important;
                    bottom: 0 !important;
                    left: 0 !important;
                    width: 100% !important;
                    height: 100% !important;
                }
            `;
                document.head.appendChild(styleElement);
            };

            const removeCanvasActiveStyles = () => {
                const styleElement = document.getElementById('canvas-active-styles');
                if (styleElement) {
                    document.head.removeChild(styleElement);
                }
            };

            // Handle fullscreen changes
            container.addEventListener('fullscreenchange', () => {
                const canvas = document.querySelector('canvas');
                if (document.fullscreenElement) {
                    if (!canvas.classList.contains('canvas-active')) {
                        addCanvasActiveStyles();
                        canvas.classList.add('canvas-active');
                    }
                } else {
                    if (canvas.classList.contains('canvas-active')) {
                        canvas.classList.remove('canvas-active');
                        removeCanvasActiveStyles();
                    }
                }
            });

            {{{makeDynCall('v', 'callbackPtr')}}}();
        });
    },

    ModalCallAsync__deps: ['$ExecuteCallAsync'],
    ModalCallAsync: async function (id, methodNameStrPtr, parameterStrPtr, callbackPtr) {
        const callFn = async (appKitConfig, methodName, parameterObj) => {
            return await appKitConfig.modal[methodName](parameterObj);
        };
        await ExecuteCallAsync(callFn, id, methodNameStrPtr, parameterStrPtr, callbackPtr);
    },

    ModalCall__deps: ['$ExecuteCall'],
    ModalCall: function (methodNameStrPtr, parameterStrPtr) {
        const callFn = (appKitConfig, methodName, parameterObj) => {
            return appKitConfig.modal[methodName](parameterObj);
        };
        return ExecuteCall(callFn, methodNameStrPtr, parameterStrPtr);
    },

    WagmiCallAsync__deps: ['$ExecuteCallAsync'],
    WagmiCallAsync: async function (id, methodNameStrPtr, parameterStrPtr, callbackPtr) {
        const callFn = async (appKitConfig, methodName, parameterObj) => {
            return await appKitConfig.wagmiCore[methodName](appKitConfig.config, parameterObj);
        };
        await ExecuteCallAsync(callFn, id, methodNameStrPtr, parameterStrPtr, callbackPtr);
    },
    
    WagmiCall__deps: ['$ExecuteCall'],
    WagmiCall: function (methodNameStrPtr, parameterStrPtr) {
        const callFn = (appKitConfig, methodName, parameterObj) => {
            return appKitConfig.wagmiCore[methodName](appKitConfig.config, parameterObj);
        };
        return ExecuteCall(callFn, methodNameStrPtr, parameterStrPtr);
    },
    
    ViemCallAsync__deps: ['$ExecuteCallAsync'],
    ViemCallAsync: async function (id, methodNameStrPtr, parameterStrPtr, callbackPtr) {
        const callFn = async (appKitConfig, methodName, parameterObj) => {
            return await appKitConfig.viem[methodName](parameterObj);
        };
        await ExecuteCallAsync(callFn, id, methodNameStrPtr, parameterStrPtr, callbackPtr);
    },
    
    ViemCall__deps: ['$ExecuteCall'],
    ViemCall: function (methodNameStrPtr, parameterStrPtr) {
        const callFn = (appKitConfig, methodName, parameterObj) => {
            return appKitConfig.viem[methodName](parameterObj);
        };
        return ExecuteCall(callFn, methodNameStrPtr, parameterStrPtr);
    },

    WagmiWatchAccount__deps: ['$SerializeJson'],
    WagmiWatchAccount: function (callbackPtr) {
        _appKitConfig.wagmiCore.watchAccount(_appKitConfig.config, {
            onChange(data) {
                const dataStr = stringToNewUTF8(SerializeJson(data));
                {{{makeDynCall('vi', 'callbackPtr')}}}(dataStr);
                _free(dataStr);
            }
        });
    },

    WagmiWatchChainId__deps: ['$SerializeJson'],
    WagmiWatchChainId: function (callbackPtr) {
        _appKitConfig.wagmiCore.watchChainId(_appKitConfig.config, {
            onChange(data) {
                const dataStr = stringToNewUTF8(SerializeJson(data));
                {{{makeDynCall('vi', 'callbackPtr')}}}(dataStr);
                _free(dataStr);
            }
        });
    },

    ModalSubscribeState__deps: ['$SerializeJson'],
    ModalSubscribeState: function (callbackPtr) {
        _appKitConfig.modal.subscribeState(newState => {
            const json = SerializeJson(newState);
            const dataStr = stringToNewUTF8(json);
            {{{makeDynCall('vi', 'callbackPtr')}}}(dataStr);
            _free(dataStr);
        });
    },

    ModalSubscribeAccount__deps: ['$SerializeJson'],
    ModalSubscribeAccount: function (callbackPtr) {
        _appKitConfig.modal.subscribeAccount(newState => {
            const json = SerializeJson(newState);
            const dataStr = stringToNewUTF8(json);
            {{{makeDynCall('vi', 'callbackPtr')}}}(dataStr);
            _free(dataStr);
        });
    },
});
