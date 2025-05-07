# Nethereum.Unity

Nethereum 472 / Netstandard AOT runtime dlls, please check Nethereum releases for more information, extra components, or other versions (net461, net35, netstandard)  https://github.com/Nethereum/Nethereum/releases.

Example template can be found at https://github.com/Nethereum/Unity3dSampleTemplate.

_Note_ If you are targeting WegGl and want to use async / await you will require the WebGlThreadPatcher see example above.

## Package installation instructions

#### Install via Package Manager using OpenUpm

* open Edit/Project Settings/Package Manager
* add a new Scoped Registry (or edit the existing OpenUPM entry)
  
    **Name** package.openupm.com

    **URL** https://package.openupm.com

    **Scope(s)** com.nethereum.unity
* click Save or Apply
* open Window/Package Manager
* click +
* select Add package by name... or Add package from git URL...
* paste com.nethereum.unity into name
* paste 4.19.2 into version (or your preferred one)
* click Add


Alternatively, merge the snippet to Packages/manifest.json (https://docs.unity3d.com/Manual/upm-manifestPrj.html)
```json
{
    "scopedRegistries": [
        {
            "name": "package.openupm.com",
            "url": "https://package.openupm.com",
            "scopes": [
              "com.nethereum.unity"
            ]
        }
    ],
    "dependencies": {
        "com.nethereum.unity": "4.19.2"
    }
}
```

### Install using Git

To add this package as a dependency just use the github url, more info can be found here: https://docs.unity3d.com/Manual/upm-git.html
