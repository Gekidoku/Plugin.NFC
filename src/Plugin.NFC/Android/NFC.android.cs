﻿using Android;
using Android.App;
using Android.Content;
using Android.Nfc;
using Android.Nfc.Tech;
using Android.OS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Android.Icu.Text.AlphabeticIndex;

namespace Plugin.NFC
{
	/// <summary>
	/// Android implementation of <see cref="INFC"/>
	/// </summary>
	public class NFCImplementation : INFC
	{
		public event EventHandler OnTagConnected;
		public event EventHandler OnTagDisconnected;
        List<NdefMessageReceivedEventHandler> delegates = new List<NdefMessageReceivedEventHandler>();
        private event NdefMessageReceivedEventHandler OnMessageReceivedReal;

        public event NdefMessageReceivedEventHandler OnMessageReceived
        {
            add
            {
                OnMessageReceivedReal += value;
                delegates.Add(value);

            }
            remove
            {
                OnMessageReceivedReal -= value;
                delegates.Remove(value);
            }
        }
		public event NdefMessagePublishedEventHandler OnMessagePublished;
		public event TagDiscoveredEventHandler OnTagDiscovered;
		public event EventHandler OniOSReadingSessionCancelled;
		public event TagListeningStatusChangedEventHandler OnTagListeningStatusChanged;

        public void RemoveAllDelegates()
        {
            foreach(NdefMessageReceivedEventHandler eh in delegates)
            {
                OnMessageReceivedReal -= eh;
            }
            delegates.Clear();
        }

		readonly NfcAdapter _nfcAdapter;

		bool _isListening;
		bool _isWriting;
		bool _isFormatting;
		Tag _currentTag;

		/// <summary>
		/// Current Android <see cref="Context"/>
		/// </summary>
		Context CurrentContext => CrossNFC.AppContext;

		/// <summary>
		/// Current Android <see cref="Activity"/>
		/// </summary>
		Activity CurrentActivity => CrossNFC.GetCurrentActivity(true);

		/// <summary>
		/// Checks if NFC Feature is available
		/// </summary>
		public bool IsAvailable
		{
			get
			{
				if (CurrentContext.CheckCallingOrSelfPermission(Manifest.Permission.Nfc) != Android.Content.PM.Permission.Granted)
					return false;
				return _nfcAdapter != null;
			}
		}

		/// <summary>
		/// Checks if NFC Feature is enabled
		/// </summary>
		public bool IsEnabled => IsAvailable && _nfcAdapter.IsEnabled;

		/// <summary>
		/// Checks if writing mode is supported
		/// </summary>
		public bool IsWritingTagSupported => NFCUtils.IsWritingSupported();

		/// <summary>
		/// NFC configuration
		/// </summary>
		public NfcConfiguration Configuration { get; private set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public NFCImplementation()
		{
			_nfcAdapter = NfcAdapter.GetDefaultAdapter(CurrentContext);
			Configuration = NfcConfiguration.GetDefaultConfiguration();
           
		}

		/// <summary>
		/// Update NFC configuration
		/// </summary>
		/// <param name="configuration"><see cref="NfcConfiguration"/></param>
		public void SetConfiguration(NfcConfiguration configuration) => Configuration.Update(configuration);

		/// <summary>
		/// Starts tags detection
		/// </summary>
		public void StartListening(string[][] techList = null)
		{
			if (_nfcAdapter == null)
				return;

			var intent = new Intent(CurrentActivity, CurrentActivity.GetType()).AddFlags(ActivityFlags.SingleTop);

			// We don't use MonoAndroid12.0 as targetframework for easier backward compatibility:
			// MonoAndroid12.0 needs JDK 11.
			PendingIntentFlags pendingIntentFlags = 0;

#if NET6_0_OR_GREATER
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.S)
                pendingIntentFlags = PendingIntentFlags.Mutable;
#else
            if ((int)Android.OS.Build.VERSION.SdkInt >= 31) //Android.OS.BuildVersionCodes.S
				pendingIntentFlags = (PendingIntentFlags)33554432; //PendingIntentFlags.Mutable
#endif

            var pendingIntent = PendingIntent.GetActivity(CurrentActivity, 0, intent, pendingIntentFlags);

			var ndefFilter = new IntentFilter(NfcAdapter.ActionNdefDiscovered);
			ndefFilter.AddDataType("*/*");

			var tagFilter = new IntentFilter(NfcAdapter.ActionTagDiscovered);
			tagFilter.AddCategory(Intent.CategoryDefault);

			var filters = new IntentFilter[] { ndefFilter, tagFilter };

            if (CurrentActivity is not null && !CurrentActivity.IsDestroyed && !CurrentActivity.IsFinishing)
            {
                _nfcAdapter.EnableForegroundDispatch(CurrentActivity, pendingIntent, filters, techList);


                _isListening = true;
                OnTagListeningStatusChanged?.Invoke(_isListening);
            }
            else
            {
                _isListening = false;
                OnTagListeningStatusChanged?.Invoke(_isListening);
            }
		}



		/// <summary>
		/// Stops tags detection
		/// </summary>
		public void StopListening()
		{
			DisablePublishing();
			if (_nfcAdapter != null)
				_nfcAdapter.DisableForegroundDispatch(CurrentActivity);

			_isListening = false;
			OnTagListeningStatusChanged?.Invoke(_isListening);
		}

		/// <summary>
		/// Starts tag publishing (writing or formatting)
		/// </summary>
		/// <param name="clearMessage">Format tag</param>
		public void StartPublishing(bool clearMessage = false)
		{
			if (!IsWritingTagSupported)
				return;

			_isWriting = true;
			_isFormatting = clearMessage;
		}

		/// <summary>
		/// Stops tag publishing
		/// </summary>
		public void StopPublishing() => DisablePublishing();

		/// <summary>
		/// Publish or write a message on a tag
		/// </summary>
		/// <param name="tagInfo">see <see cref="ITagInfo"/></param>
		/// <param name="makeReadOnly">make tag read-only</param>
		public void PublishMessage(ITagInfo tagInfo, bool makeReadOnly = false) => WriteMessageAndFormat(tagInfo, makeReadOnly);

		/// <summary>
		/// Format tag
		/// </summary>
		/// <param name="tagInfo">see <see cref="ITagInfo"/></param>
		public void ClearMessage(ITagInfo tagInfo) => WriteOrClearMessage(tagInfo, true);


        internal void WriteMessageAndFormat(ITagInfo tagInfo, bool makeReadOnly = false)
        {
            //Credit to Pozone Alessandro for the starting point of this function 
            //https://github.com/poz1/NFCForms

            if (_currentTag == null)
            {
                throw new Exception(Configuration.Messages.NFCErrorMissingTag);
            }

            Ndef ndef = Ndef.Get(_currentTag);


            //Check if the tag can be NDEFformatted
            var formatcheck = _currentTag.GetTechList().Contains("android.nfc.tech.NdefFormatable");

            //Previous check didnt compare both just ndef being null can also mean its not formatted
            if (ndef == null && formatcheck == false)
            {
                throw new Exception(Configuration.Messages.NFCErrorNotCompliantTag);
            }
            //Not formatted but can be formatted
            if (ndef == null && formatcheck == true)
            {


                NdefFormatable formatable = NdefFormatable.Get(_currentTag);
                //For some reason this could still give null even if formatcheck gave a true so /shrug
                if (formatable == null)
                {
                    throw new Exception(Configuration.Messages.NFCErrorNotCompliantTag);
                }
                try
                {
                    formatable.Connect();

                    OnTagConnected?.Invoke(null, EventArgs.Empty);
                }
                catch
                {
                    throw new Exception(Configuration.Messages.NFCErrorMissingTag);
                }
                //old size check.
                //int size = message.ToByteArray().Length;


                try
                {
                    List<Android.Nfc.NdefRecord> records = new List<Android.Nfc.NdefRecord>();
                    for (int i = 0; i < tagInfo.Records.Length; i++)
                    {
                        var record = tagInfo.Records[i];
                        if (GetAndroidNdefRecord(record) is NdefRecord ndefRecord)
                            records.Add(ndefRecord);
                    };
                    Android.Nfc.NdefMessage msg = new Android.Nfc.NdefMessage(records.ToArray());
                    //This Format works with Mifare Classic and NTAG213. Fails with ultralight. these are the chips I tested it with.
                    //Both with pre used ones and with factory new ones.
                    if(makeReadOnly)
                        formatable.FormatReadOnly(msg);
                    else
                        formatable.Format(msg);
                }

                catch (TagLostException tle)
                {
                    throw new Exception(Configuration.Messages.NFCErrorMissingTag + " " + tle.Message);
                }

                catch (Android.Nfc.FormatException fe)
                {
                    throw new Exception(Configuration.Messages.NFCErrorMissingTag + " " + fe.Message);
                }

                catch (Exception e)
                {
                    //Assume that if we are here the tag is still touching the device, our message is correct, and the tag isnt busted.
                    try
                    {
                        //we close this tech so we can open it as ultralight
                        formatable.Close();
                        //check if the connected chip is ultralight.
                        if (_currentTag.GetTechList().Contains("android.nfc.tech.MifareUltralight"))
                        {

                            MifareUltralight ultralight = MifareUltralight.Get(_currentTag);
                            if (ultralight != null)
                            {
                                try
                                {
                                    //connect with ultralight tech
                                    ultralight.Connect();
                                    //Found this on a stackoverflow question that had the same problem, apparently this sets it up to be NFC Forum Type 2 tag.
                                    //https://stackoverflow.com/questions/35985287/formatting-a-mifare-ultralight-to-ndef-throw-io-exception
                                    ultralight.Transceive(new byte[]
                                    {
                                    (byte)0xA2,//Write
                                    (byte)0x03,//Page Nr = 3
                                    (byte)0xE1,(byte)0x10,(byte)0x06, (byte)0x00//capability container (mapping version 1.0, 48 bytes for data available, read/ write allowed)
                                    });
                                    ultralight.Transceive(new byte[]
                                    {
                                    (byte)0xA2,//Write 
                                    (byte)0x04,//Page nr = 4
                                    (byte)0x03, (byte)0x00,(byte)0xFE,(byte)0x00 // empty NDEF TLV, Terminator TLV
                                    });
                                }
                                catch (Exception e1)
                                {

                                }
                                finally
                                {
                                    //Now the tag is formatted to accept NDEF
                                    try
                                    {
                                        //Close this tech connection as this one doesnt have WriteNdef
                                        ultralight.Close();
                                        //Get the tag as Ndef
                                        ndef = Ndef.Get(_currentTag);
                                        //Connect
                                        ndef.Connect();
                                        //Check the message again JIC
                                        List<Android.Nfc.NdefRecord> recordsFormat = new List<Android.Nfc.NdefRecord>();
                                        for (int i = 0; i < tagInfo.Records.Length; i++)
                                        {
                                            var record = tagInfo.Records[i];
                                            if (GetAndroidNdefRecord(record) is NdefRecord ndefRecord)
                                                recordsFormat.Add(ndefRecord);
                                        };

                                        Android.Nfc.NdefMessage msg = new Android.Nfc.NdefMessage(recordsFormat.ToArray());
                                        //Write and close
                                       
                                        ndef.WriteNdefMessage(msg);
                                        if (makeReadOnly)
                                            ndef.MakeReadOnly();
                                        ndef.Close();
                                    }
                                    catch
                                    {

                                    }

                                }
                            }
                        }

                    }
                    catch (Exception eform)
                    {
                         throw new Exception(Configuration.Messages.NFCErrorWrite + " " + eform.ToString());
                        
                    }

                }

                finally
                {
                    //Used to not have the ultralight code in so i had to close here
                    //formatable.Close();
                    OnTagDisconnected?.Invoke(null, EventArgs.Empty);
                }

            }
            else
            {
                //Rest as normal
                try
                {
                    ndef.Connect();
                    OnTagConnected?.Invoke(null, EventArgs.Empty);
                }

                catch
                {
                    throw new Exception("Tag Error: No Tag nearby");
                }

                if (!ndef.IsWritable)
                {
                    ndef.Close();
                    return;
                }

                
                    try
                    {
                        List<Android.Nfc.NdefRecord> records = new List<Android.Nfc.NdefRecord>();
                        for (int i = 0; i < tagInfo.Records.Length; i++)
                        {
                        var record = tagInfo.Records[i];
                        if (GetAndroidNdefRecord(record) is NdefRecord ndefRecord)
                            records.Add(ndefRecord);
                        }
                        Android.Nfc.NdefMessage msg = new Android.Nfc.NdefMessage(records.ToArray());
                        ndef.WriteNdefMessage(msg);
                        if (makeReadOnly)
                            ndef.MakeReadOnly();
                    }

                    catch (TagLostException tle)
                    {
                        throw new Exception("Tag Lost Error: " + tle.Message);
                    }


                  

                    catch (Android.Nfc.FormatException fe)
                    {
                        throw new Exception("Tag Format Error: " + fe.Message);
                    }

                    catch (Exception e)
                    {
                        throw new Exception("Tag Error: " + e.ToString());
                    }

                    finally
                    {
                        ndef.Close();
                        OnTagConnected?.Invoke(null, EventArgs.Empty);
                    }
                
            }
        }

            /// <summary>
            /// Write or Clear a NDEF message
            /// </summary>
            /// <param name="tagInfo"><see cref="ITagInfo"/></param>
            /// <param name="clearMessage">Clear Message</param>
            /// <param name="makeReadOnly">Make tag read-only</param>
            internal void WriteOrClearMessage(ITagInfo tagInfo, bool clearMessage = false, bool makeReadOnly = false)
		{
			try
			{
				if (_currentTag == null)
					throw new Exception(Configuration.Messages.NFCErrorMissingTag);

				if (tagInfo == null)
					throw new Exception(Configuration.Messages.NFCErrorMissingTagInfo);

				var ndef = Ndef.Get(_currentTag);
                if (ndef != null)
                {
                    try
                    {
                        if (!ndef.IsWritable)
                            throw new Exception(Configuration.Messages.NFCErrorReadOnlyTag);

                        if (ndef.MaxSize < NFCUtils.GetSize(tagInfo.Records))
                            throw new Exception(Configuration.Messages.NFCErrorCapacityTag);

                        ndef.Connect();
                        OnTagConnected?.Invoke(null, EventArgs.Empty);

                        NdefMessage message = null;
                        if (clearMessage)
                        {
                            message = GetEmptyNdefMessage();
                        }
                        else
                        {
                            var records = new List<NdefRecord>();
                            for (var i = 0; i < tagInfo.Records.Length; i++)
                            {
                                var record = tagInfo.Records[i];
                                if (GetAndroidNdefRecord(record) is NdefRecord ndefRecord)
                                    records.Add(ndefRecord);
                            }

                            if (records.Any())
                                message = new NdefMessage(records.ToArray());
                        }

                        if (message != null)
                        {
                            ndef.WriteNdefMessage(message);

                            if (!clearMessage && makeReadOnly)
                            {
                                if (!MakeReadOnly(ndef))
                                    Console.WriteLine("Cannot lock tag");
                            }

                            var nTag = GetTagInfo(_currentTag, ndef.NdefMessage);
                            OnMessagePublished?.Invoke(nTag);
                        }
                        else
                            throw new Exception(Configuration.Messages.NFCErrorWrite);
                    }
                    catch (Android.Nfc.TagLostException tlex)
                    {
                        throw new Exception("Tag Lost Error: " + tlex.Message);
                    }
                    catch (Java.IO.IOException ioex)
                    {
                        throw new Exception("Tag IO Error: " + ioex.Message);
                    }
                    catch (Android.Nfc.FormatException fe)
                    {
                        throw new Exception("Tag Format Error: " + fe.Message);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Tag Error:" + ex.Message);
                    }
                    finally
                    {
                        if (ndef.IsConnected)
                            ndef.Close();

                        _currentTag = null;
                        OnTagDisconnected?.Invoke(null, EventArgs.Empty);
                    }
                }
                else
                {
                   
                              
                               
                               
                     
                        
                            throw new Exception(Configuration.Messages.NFCErrorNotCompliantTag);
                        
                    
                }
					
			}
			catch (Exception ex)
			{
				StopPublishingAndThrowError(ex.Message);
			}
		}

		/// <summary>
		/// Handle Android OnNewIntent
		/// </summary>
		/// <param name="intent">Android <see cref="Intent"/></param>
		internal void HandleNewIntent(Intent intent)
		{
			if (intent == null)
				return;

			if (intent.Action == NfcAdapter.ActionTagDiscovered || intent.Action == NfcAdapter.ActionNdefDiscovered)
			{
				_currentTag = intent.GetParcelableExtra(NfcAdapter.ExtraTag) as Tag;
				if (_currentTag != null)
				{
					var nTag = GetTagInfo(_currentTag);
					if (_isWriting)
					{
						// Write mode
						OnTagDiscovered?.Invoke(nTag, _isFormatting);
					}
					else
					{
                        // Read mode
                        OnMessageReceivedReal?.Invoke(nTag);
					}
				}
			}
		}




		/// <summary>
		/// Handle Android OnResume
		/// </summary>
		internal void HandleOnResume()
		{
			// Android 10 fix:
			// If listening mode is already enable, we restart listening when activity is resumed
			if (_isListening)
				StartListening();
		}

        #region Private

		/// <summary>
		/// Stops publishing and throws error
		/// </summary>
		/// <param name="message">message</param>
		void StopPublishingAndThrowError(string message)
		{
			StopPublishing();
			throw new Exception(message);
		}

		/// <summary>
		/// Deactivate publishing
		/// </summary>
		void DisablePublishing()
		{
			_isWriting = false;
			_isFormatting = false;
		}

		/// <summary>
		/// Transforms an array of <see cref="NdefRecord"/> into an array of <see cref="NFCNdefRecord"/>
		/// </summary>
		/// <param name="records">Array of <see cref="NdefRecord"/></param>
		/// <returns>Array of <see cref="NFCNdefRecord"/></returns>
		NFCNdefRecord[] GetRecords(NdefRecord[] records)
		{
			var results = new NFCNdefRecord[records.Length];
			for (var i = 0; i < records.Length; i++)
			{
				var ndefRecord = new NFCNdefRecord
				{
					TypeFormat = (NFCNdefTypeFormat)records[i].Tnf,
					Uri = records[i].ToUri()?.ToString(),
					MimeType = records[i].ToMimeType(),
					Payload = records[i].GetPayload()
				};
				results.SetValue(ndefRecord, i);
			}
			return results;
		}

		/// <summary>
		/// Returns informations contains in NFC Tag
		/// </summary>
		/// <param name="tag">Android <see cref="Tag"/></param>
		/// <param name="ndefMessage">Android <see cref="NdefMessage"/></param>
		/// <returns><see cref="ITagInfo"/></returns>
		ITagInfo GetTagInfo(Tag tag, NdefMessage ndefMessage = null)
		{
			if (tag == null)
				return null;

			var ndef = Ndef.Get(tag);
			var nTag = new TagInfo(tag.GetId(), ndef != null);
            nTag.IsNdefFormatable = tag.GetTechList().Contains("android.nfc.tech.NdefFormatable");
            if (ndef != null)
			{
				nTag.Capacity = ndef.MaxSize;
				nTag.IsWritable = ndef.IsWritable;
                
                if (ndefMessage == null)
					ndefMessage = ndef.CachedNdefMessage;

				if (ndefMessage != null)
				{
					var records = ndefMessage.GetRecords();
					nTag.Records = GetRecords(records);
				}
			}

			return nTag;
		}

		/// <summary>
		/// Transforms a <see cref="NFCNdefRecord"/> into an Android <see cref="NdefRecord"/>
		/// </summary>
		/// <param name="record">Object <see cref="NFCNdefRecord"/></param>
		/// <returns>Android <see cref="NdefRecord"/></returns>
		NdefRecord GetAndroidNdefRecord(NFCNdefRecord record)
		{
			if (record == null)
				return null;

			NdefRecord ndefRecord = null;
			switch (record.TypeFormat)
			{
				case NFCNdefTypeFormat.WellKnown:
					var languageCode = record.LanguageCode;
					if (string.IsNullOrWhiteSpace(languageCode)) languageCode = Configuration.DefaultLanguageCode;
					ndefRecord = NdefRecord.CreateTextRecord(languageCode.Substring(0, 2), Encoding.UTF8.GetString(record.Payload));
					break;
				case NFCNdefTypeFormat.Mime:
					ndefRecord = NdefRecord.CreateMime(record.MimeType, record.Payload);
					break;
				case NFCNdefTypeFormat.Uri:
					ndefRecord = NdefRecord.CreateUri(Encoding.UTF8.GetString(record.Payload));
					break;
				case NFCNdefTypeFormat.External:
					ndefRecord = NdefRecord.CreateExternal(record.ExternalDomain, record.ExternalType, record.Payload);
					break;
				case NFCNdefTypeFormat.Empty:
					ndefRecord = GetEmptyNdefRecord();
					break;
				case NFCNdefTypeFormat.Unknown:
				case NFCNdefTypeFormat.Unchanged:
				case NFCNdefTypeFormat.Reserved:
				default:
					break;

			}
			return ndefRecord;
		}

		/// <summary>
		/// Returns an empty Android <see cref="NdefRecord"/>
		/// </summary>
		/// <returns>Android <see cref="NdefRecord"/></returns>
		NdefRecord GetEmptyNdefRecord()
		{
			var empty = Array.Empty<byte>();
			return new NdefRecord(NdefRecord.TnfEmpty, empty, empty, empty);
		}

		/// <summary>
		/// Returns an empty Android <see cref="NdefMessage"/>
		/// </summary>
		/// <returns>Android <see cref="NdefMessage"/></returns>
		NdefMessage GetEmptyNdefMessage()
		{
			var records = new NdefRecord[1];
			records[0] = GetEmptyNdefRecord();
			return new NdefMessage(records);
		}

		/// <summary>
		/// Make a tag read-only
		/// WARNING: This operation is permanent
		/// </summary>
		/// <param name="ndef"><see cref="Ndef"/></param>
		/// <returns>boolean</returns>
		bool MakeReadOnly(Ndef ndef)
		{
			if (ndef == null)
				return false;

			var result = false;
			var newConnection = false;

			if (!ndef.IsConnected)
			{
				newConnection = true;
				ndef.Connect();
			}

			if (ndef.CanMakeReadOnly())
				result = ndef.MakeReadOnly();

			if (newConnection && ndef.IsConnected)
				ndef.Close();

			return result;
		}

        #endregion

        #region NFC Status Event Listener

		NfcBroadcastReceiver _nfcBroadcastReceiver;

		event OnNfcStatusChangedEventHandler _onNfcStatusChangedInternal;

		public event OnNfcStatusChangedEventHandler OnNfcStatusChanged
		{
			add
			{
				var wasRunning = _onNfcStatusChangedInternal != null;
				_onNfcStatusChangedInternal += value;
				if (!wasRunning && _onNfcStatusChangedInternal != null)
				{
					RegisterListener();
				}
			}
			remove
			{
				var wasRunning = _onNfcStatusChangedInternal != null;
				_onNfcStatusChangedInternal -= value;
				if (wasRunning && _onNfcStatusChangedInternal == null)
				{
					UnRegisterListener();
				}
			}
		}

		/// <summary>
		/// Register NFC Broadcast Receiver
		/// </summary>
		void RegisterListener()
		{
			_nfcBroadcastReceiver = new NfcBroadcastReceiver(OnNfcStatusChange);
			CurrentContext?.RegisterReceiver(_nfcBroadcastReceiver, new IntentFilter(NfcAdapter.ActionAdapterStateChanged));
		}

		/// <summary>
		/// Unregister NFC Broadcast Receiver
		/// </summary>
		void UnRegisterListener()
		{
			if (_nfcBroadcastReceiver == null)
				return;

			try
			{
				CurrentContext?.UnregisterReceiver(_nfcBroadcastReceiver);
			}
			catch (Java.Lang.IllegalArgumentException ex)
			{
				throw new Exception("NFC Broadcast Receiver Error: " + ex.Message);
			}

			_nfcBroadcastReceiver.Dispose();
			_nfcBroadcastReceiver = null;
		}

		/// <summary>
		/// Called when NFC status has changed
		/// </summary>
		void OnNfcStatusChange() => _onNfcStatusChangedInternal?.Invoke(IsEnabled);

		/// <summary>
		/// Broadcast Receiver to check NFC feature availability
		/// </summary>
		[BroadcastReceiver(Enabled = true, Exported = false, Label = "NFC Status Broadcast Receiver")]
		class NfcBroadcastReceiver : BroadcastReceiver
		{
			Action _onChanged;

			public NfcBroadcastReceiver() { }
			public NfcBroadcastReceiver(Action onChanged)
			{
				_onChanged = onChanged;
			}

			public override async void OnReceive(Context context, Intent intent)
			{
				if (intent.Action == NfcAdapter.ActionAdapterStateChanged)
				{
					var state = intent.GetIntExtra(NfcAdapter.ExtraAdapterState, default);
					if (state == NfcAdapter.StateOff || state == NfcAdapter.StateOn)
					{
						// await 1500ms to ensure that the status updates
						await Task.Delay(1500);
						_onChanged?.Invoke();
					}
				}
			}
		}

        #endregion

	}
}
