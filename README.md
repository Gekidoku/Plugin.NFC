# ![NFC logo][logo] Plugin.NFC
A Cross-Platform NFC (_Near Field Communication_) plugin to easily read and write NFC tags in your Xamarin Forms or .NET MAUI applications.

This plugin uses **NDEF** (_NFC Data Exchange Format_) for maximum compatibilty between NFC devices, tag types, and operating systems.


> CI Feed : https://www.myget.org/F/plugin-nfc/api/v3/index.json

## Supported Platforms
Platform|Version|Tested on
:---|:---|:---
Android|4.4+|Google Nexus 5, Huawei Mate 10 Pro, Google Pixel 4a, Google Pixel 6a
iOS|11+|iPhone 7, iPhone 8

> Windows, Mac and Linux are currently not supported. Pull Requests are welcomed! 

## Setup
### Android Specific
* Add NFC Permission `android.permission.NFC` and NFC feature `android.hardware.nfc` in your `AndroidManifest.xml`
```xml
<uses-permission android:name="android.permission.NFC" />
<uses-feature android:name="android.hardware.nfc" android:required="false" />
```

* Add the line `CrossNFC.Init(this)` in your `OnCreate()`
```csharp
protected override void OnCreate(Bundle savedInstanceState)
{
    // Plugin NFC: Initialization before base.OnCreate(...) (Important on .NET MAUI)
    CrossNFC.Init(this);

    base.OnCreate(savedInstanceState);
    [...]
}
```
* Add the line `CrossNFC.OnResume()` in your `OnResume()`
```csharp
protected override void OnResume()
{
    base.OnResume();

    // Plugin NFC: Restart NFC listening on resume (needed for Android 10+) 
    CrossNFC.OnResume();
}
```

* Add the line `CrossNFC.OnNewIntent(intent)` in your `OnNewIntent()`
```csharp
protected override void OnNewIntent(Intent intent)
{
    base.OnNewIntent(intent);

    // Plugin NFC: Tag Discovery Interception
    CrossNFC.OnNewIntent(intent);
}
```

### iOS Specific

> iOS 13+ is required for writing tags.

An iPhone 7+ and iOS 11+ are required in order to use NFC with iOS devices.

* Add `Near Field Communication Tag Reading` capabilty in your `Entitlements.plist`
```xml
<key>com.apple.developer.nfc.readersession.formats</key>
<array>
    <string>NDEF</string>
    <string>TAG</string>
</array>
```

* Add a NFC feature description in your Info.plist
```xml
<key>NFCReaderUsageDescription</key>
<string>NFC tag to read NDEF messages into the application</string>
```

* Add these lines in your Info.plist if you want to interact with ISO 7816 compatible tags
```xml
<key>com.apple.developer.nfc.readersession.iso7816.select-identifiers</key>
<string>com.apple.developer.nfc.readersession.iso7816.select-identifiers</string>
```

* Add these lines in your Info.plist if you want to interact with Mifare Desfire EV3 compatible tags
```xml
<key>com.apple.developer.nfc.readersession.iso7816.select-identifiers</key>
<array>
    <string>com.apple.developer.nfc.readersession.iso7816.select-identifiers</string>
    <string>D2760000850100</string>
</array>
```

#### iOS Considerations

If you are having issues reading Mifare Classic 1K cards - the chances are the issue is not with this library, but with Apple's API.

On iOS 11, apple released the ability to READ NFC NDEF data only using the NfcNdefReaderSession API (https://developer.apple.com/documentation/corenfc/nfcndefreadersession)

A Mifare Classic 1K card will scan if there is a valid NDEF record on it. A blank card will not scan.

In iOS 11, it was not possible to obtain the CSN (serial number/identity) from NFC tags/card.

With iOS 13, along came the ability to write NDEF data AND read serial numbers. However, rather then adapting the NfcNdefReaderSession API, Apple created a NEW API called NfcTagReaderSession (https://developer.apple.com/documentation/corenfc/nfctagreadersession) and left the old NfcNdefReaderSession API untouched.

The new NfcTagReaderSession API in iOS 13 no longer supports Mifare Classic 1K cards period. No idea why - but if you look at Apple's Dev Forums multiple people have spotted the same thing.

So even if you have a Mifare Classic 1K card which reads fine with the old iOS 11 NfcNdefReaderSession API, that same card will not even scan with iOS 13's NfcTagReaderSession API.

If you need to read NDEF data off of a Mifare Classic 1K card, then you can:
-  use version 0.1.11 of this library as it was written with the NfcNdefReaderSession API.
-  use `CrossNFC.Legacy` from 0.1.20+ which allow you to switch between NfcTagReaderSession and NfcNdefReaderSession on-the-fly.

Unfortunately, even with iOS 13, it seems there is no way to read the serial number / CSN off of a Mifare Classic 1K card.

## API Usage

Before to use the plugin, please check if NFC feature is supported by the platform using `CrossNFC.IsSupported`.

To get the current platform implementation of the plugin, please call `CrossNFC.Current`:
* Check `CrossNFC.Current.IsAvailable` to verify if NFC is available.
* Check `CrossNFC.Current.IsEnabled` to verify if NFC is enabled. 
* Register events:
```csharp
// Event raised when a ndef message is received.
CrossNFC.Current.OnMessageReceived += Current_OnMessageReceived;
// Event raised when a ndef message has been published.
CrossNFC.Current.OnMessagePublished += Current_OnMessagePublished;
// Event raised when a tag is discovered. Used for publishing.
CrossNFC.Current.OnTagDiscovered += Current_OnTagDiscovered;
// Event raised when NFC listener status changed
CrossNFC.Current.OnTagListeningStatusChanged += Current_OnTagListeningStatusChanged;

// Android Only:
// Event raised when NFC state has changed.
CrossNFC.Current.OnNfcStatusChanged += Current_OnNfcStatusChanged;

// iOS Only: 
// Event raised when a user cancelled NFC session.
CrossNFC.Current.OniOSReadingSessionCancelled += Current_OniOSReadingSessionCancelled;
```


### Read a tag
* Start listening with `CrossNFC.Current.StartListening()`.
* When a NDEF message is received, the event `OnMessageReceived` is raised.

### Write a tag
* To write a tag, call `CrossNFC.Current.StartPublishing()`
* Then `CrossNFC.Current.PublishMessage(ITagInfo)` when `OnTagDiscovered` event is raised. 
* Do not forget to call `CrossNFC.Current.StopPublishing()` once the tag has been written.

### Clear a tag
* To clear a tag, call `CrossNFC.Current.StartPublishing(clearMessage: true)`
* Then `CrossNFC.Current.PublishMessage(ITagInfo)` when `OnTagDiscovered` event is raised.
* Do not forget to call `CrossNFC.Current.StopPublishing()` once the tag has been cleared.


## Contributing
Feel free to contribute. PRs are accepted and welcomed.

## Credits
Inspired by the great work of many developers. Many thanks to:
- James Montemagno ([@jamesmontemagno](https://github.com/jamesmontemagno)).
- Matthew Leibowitz ([@mattleibow](https://github.com/mattleibow)) for [Xamarin.Essentials PR #131](https://github.com/xamarin/Essentials/pull/131).
- Alessandro Pozone ([@poz1](https://github.com/poz1)) for [NFCForms](https://github.com/poz1/NFCForms).
- Ultz ([@ultz](https://github.com/Ultz)) for [XNFC](https://github.com/Ultz/XNFC).
- Sven-Michael Stübe ([@smstuebe](https://github.com/smstuebe)) for [xamarin-nfc](https://github.com/smstuebe/xamarin-nfc).

[logo]: https://github.com/franckbour/Plugin.NFC/raw/master/art/nfc48.png "NFC Logo"
