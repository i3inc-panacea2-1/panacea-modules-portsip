using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Panacea.Modules.PortSip.Native;
using Panacea.Modularity.Telephone;
using System.Reflection;
using Panacea.Core;

namespace Panacea.Modules.PortSip
{
    class PortSipTelephone : TelephoneBase, SIPCallbackEvents
    {
        PortSIPLib _sdkLib;
        bool _inited, _shouldBeRegistered;
        int _sessionId;
        string _number;
        private string _speakers;
        private string _microphone;
        private readonly ILogger _logger;

        static PortSipTelephone()
        {
            //var myPath = Path.Combine(Host.GetPluginDirectory("Telephone"), Environment.Is64BitProcess ? "x64" : "x86", "portsip_sdk.dll");
            var path = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "PortSip",
                Environment.Is64BitProcess ? "x64" : "x86",
                "portsip_sdk.dll"
                );
            LoadLibrary(path);
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr LoadLibrary(string dllToLoad);

        public PortSipTelephone(ITelephoneAccount account, ILogger logger) : base(account)
        {
            _logger = logger;
        }

        private async void Client_StateChanged(object sender, EventArgs e)
        {
            if (IsInCall)
            {
                await Task.Delay(300);
                SetAudioDevices();
            }
        }

        public override void SetAudioDevices(string speakers, string microphone)
        {
            _speakers = speakers;
            _microphone = microphone;
            SetAudioDevices();
        }
        void SetAudioDevices()
        {
            if (!_inited) return;


            int speakers = -1, microphone = -1;

            if (_speakers != null)
            {
                var split = _speakers.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                var outDevices = _sdkLib.getNumOfPlayoutDevices();
                for (var i = 0; i < outDevices; i++)
                {
                    var sb = new StringBuilder();
                    sb.Length = 256;
                    _sdkLib.getPlayoutDeviceName(i, sb, 256);

                    if (!split.Any(spl => sb.ToString().Contains(spl) || spl.Contains(sb.ToString()))) continue;

                    speakers = i;
                    break;
                }
            }
            if (_microphone != null)
            {

                var split = _microphone.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                var inDevices = _sdkLib.getNumOfRecordingDevices();

                for (var i = 0; i < inDevices; i++)
                {
                    var sb = new StringBuilder();
                    sb.Length = 256;
                    _sdkLib.getRecordingDeviceName(i, sb, 256);
                    if (!split.Any(spl => sb.ToString().Contains(spl) || spl.Contains(sb.ToString()))) continue;
                    microphone = i;
                    break;
                }
            }

            _sdkLib.setAudioDeviceId(microphone, speakers);

            // Dear friend, do not remove these lines. There is a Remedi issue where speakerphone is not 100% unless you pick up the handset and place it back.
            _sdkLib.setMicVolume(250);
            _sdkLib.setSpeakerVolume(250);
            Task.Delay(800).ContinueWith((task) =>
            {
                _sdkLib.setMicVolume(255);
                _sdkLib.setSpeakerVolume(255);
            });
            Task.Delay(1200).ContinueWith((task) =>
            {
                _sdkLib.setMicVolume(255);
                _sdkLib.setSpeakerVolume(255);
            });

        }

        public override async Task Answer(bool withVideo = false)
        {
            if (!_inited) return;

            SetAudioDevices();
            var source = new CancellationTokenSource(10000);
            await Task.Run(() =>
            {
                _sdkLib.answerCall(_sessionId, withVideo);
            }, source.Token);

            _sdkLib.muteMicrophone(false);
            OnAnswered(_number);
            IsIncoming = false;
            IsInCall = true;
            IsBusy = false;
        }

        public override async Task Call(string number, bool hasVideo = false)
        {
            if (!_inited) return;
            _number = number;
            SetAudioDevices();
            var source = new CancellationTokenSource(10000);
            await Task.Run(() =>
            {
                _sessionId = _sdkLib.call(number, true, false);
            }, source.Token);

            if (_sessionId <= 0)
            {
                return;
            }
            _sdkLib.muteMicrophone(false);
            IsBusy = true;
            OnTrying(number);
        }

        public override async Task HangUp()
        {
            if (!_inited) return;
            var source = new CancellationTokenSource(10000);
            await Task.Run(() =>
            {
                _sdkLib.hangUp(_sessionId);
            }, source.Token);

            OnClosed(_number);
            IsBusy = false;
            IsInCall = false;
            IsIncoming = false;

        }

        public override void Mute()
        {
            if (!_inited) return;
            _sdkLib.muteMicrophone(true);
        }

        #region callbacks
        public int onACTVTransferFailure(int callbackIndex, int callbackObject, int sessionId, string reason, int code)
        {
            return 0;
        }

        public int onACTVTransferSuccess(int callbackIndex, int callbackObject, int sessionId)
        {
            return 0;
        }

        public int onAudioRawCallback(IntPtr callbackObject, int sessionId, int callbackType, byte[] data, int dataLength, int samplingFreqHz)
        {
            return 0;
        }

        public int onInviteAnswered(int callbackIndex, int callbackObject, int sessionId, string callerDisplayName, string caller, string calleeDisplayName, string callee, string audioCodecNames, string videoCodecNames, bool existsAudio, bool existsVideo, string sipMessage)
        {
            IsIncoming = false;
            IsInCall = true;
            IsBusy = false;
            OnAnswered(_number);
            return 0;
        }

        public int onInviteBeginingForward(int callbackIndex, int callbackObject, string forwardTo)
        {
            return 0;
        }

        public int onInviteClosed(int callbackIndex, int callbackObject, int sessionId)
        {
            Console.WriteLine("onInviteClosed");
            if (IsIncoming && !IsInCall)
                OnMissedCall(_number);
            else if (IsInCall)
                OnClosed(_number);
            IsIncoming = false;
            IsInCall = false;
            IsBusy = false;

            return 0;
        }

        public int onInviteConnected(int callbackIndex, int callbackObject, int sessionId)
        {

            return 0;
        }

        public int onInviteFailure(int callbackIndex, int callbackObject, int sessionId, string reason, int code)
        {
            _logger.Error(this, reason);
            Console.WriteLine("onInviteFailure");
            IsIncoming = false;
            IsInCall = false;
            IsBusy = false;
            if (code == 486)
            {
                OnBusy(_number);
            }
            else
            {
                OnFailed(_number);
            }
            return 0;
        }

        public int onInviteIncoming(int callbackIndex, int callbackObject, int sessionId, string callerDisplayName, string caller, string calleeDisplayName, string callee, string audioCodecNames, string videoCodecNames, bool existsAudio, bool existsVideo, string sipMessage)
        {
            IsIncoming = true;
            IsInCall = false;
            IsBusy = true;
            _sessionId = sessionId;
            _number = callerDisplayName;
            if (string.IsNullOrEmpty(_number))
            {
                _number = caller;
                if (!string.IsNullOrEmpty(caller))
                {
                    try
                    {
                        _number = caller.Split('@')[0];
                        _number = _number.Replace("sip:", "").Trim();
                    }
                    finally
                    {

                    }
                }
            }
            CurrentNumber = _number;
            OnIncomingCall(_number);
            return 0;
        }

        public int onInviteRinging(int callbackIndex, int callbackObject, int sessionId, string statusText, int statusCode, string sipMessage)
        {
            IsInCall = false;
            IsBusy = true;
            //OnRinging(_number);
            return 0;
        }

        public int onInviteSessionProgress(int callbackIndex, int callbackObject, int sessionId, string audioCodecNames, string videoCodecNames, bool existsEarlyMedia, bool existsAudio, bool existsVideo, string sipMessage)
        {
            Console.WriteLine("onInviteSessionProgress");
            OnRinging(_number);
            return 0;
        }

        public int onInviteTrying(int callbackIndex, int callbackObject, int sessionId)
        {
            IsInCall = false;
            IsBusy = true;
            OnTrying(_number);
            return 0;
        }

        public int onInviteUpdated(int callbackIndex, int callbackObject, int sessionId, string audioCodecNames, string videoCodecNames, bool existsAudio, bool existsVideo, string sipMessage)
        {
            Console.WriteLine("onInviteUpdated");
            return 0;
        }

        public int onPlayAudioFileFinished(int callbackIndex, int callbackObject, int sessionId, string fileName)
        {
            return 0;
        }

        public int onPlayVideoFileFinished(int callbackIndex, int callbackObject, int sessionId)
        {
            return 0;
        }

        public int onPresenceOffline(int callbackIndex, int callbackObject, string fromDisplayName, string from)
        {
            return 0;
        }

        public int onPresenceOnline(int callbackIndex, int callbackObject, string fromDisplayName, string from, string stateText)
        {
            return 0;
        }

        public int onPresenceRecvSubscribe(int callbackIndex, int callbackObject, int subscribeId, string fromDisplayName, string from, string subject)
        {
            return 0;
        }

        public int onReceivedRefer(int callbackIndex, int callbackObject, int sessionId, int referId, string to, string from, string referSipMessage)
        {
            return 0;
        }

        public int onReceivedRtpPacket(IntPtr callbackObject, int sessionId, bool isAudio, byte[] RTPPacket, int packetSize)
        {
            return 0;
        }

        public int onReceivedSignaling(int callbackIndex, int callbackObject, int sessionId, StringBuilder signaling)
        {
            return 0;
        }

        public int onRecvDtmfTone(int callbackIndex, int callbackObject, int sessionId, int tone)
        {
            return 0;
        }

        public int onRecvInfo(int callbackIndex, int callbackObject, StringBuilder infoMessage)
        {
            Console.WriteLine("onRecvInfo " + infoMessage ?? "");
            return 0;
        }

        public int onRecvMessage(int callbackIndex, int callbackObject, int sessionId, string mimeType, string subMimeType, byte[] messageData, int messageDataLength)
        {
            return 0;
        }

        public int onRecvOptions(int callbackIndex, int callbackObject, StringBuilder optionsMessage)
        {
            return 0;
        }

        public int onRecvOutOfDialogMessage(int callbackIndex, int callbackObject, string fromDisplayName, string from, string toDisplayName, string to, string mimeType, string subMimeType, byte[] messageData, int messageDataLength)
        {
            return 0;
        }

        public int onReferAccepted(int callbackIndex, int callbackObject, int sessionId)
        {
            return 0;
        }

        public int onReferRejected(int callbackIndex, int callbackObject, int sessionId, string reason, int code)
        {
            Console.WriteLine("onReferRejected");
            IsInCall = false;
            IsBusy = false;
            IsIncoming = false;
            return 0;
        }

        public int onRegisterFailure(int callbackIndex, int callbackObject, string statusText, int statusCode)
        {
            if (_shouldBeRegistered)
            {

                var t = Task.Run(async () =>
                {
                    await Task.Delay(new Random().Next(5000, 15000));
                    Register();
                });
            }
            return 0;
        }

        public int onRegisterSuccess(int callbackIndex, int callbackObject, string statusText, int statusCode)
        {
            return 0;
        }

        public int onRemoteHold(int callbackIndex, int callbackObject, int sessionId)
        {
            Console.WriteLine("onRemoteHold");
            return 0;
        }

        public int onRemoteUnHold(int callbackIndex, int callbackObject, int sessionId, string audioCodecNames, string videoCodecNames, bool existsAudio, bool existsVideo)
        {
            return 0;
        }

        public int onSendingRtpPacket(IntPtr callbackObject, int sessionId, bool isAudio, byte[] RTPPacket, int packetSize)
        {
            return 0;
        }

        public int onSendingSignaling(int callbackIndex, int callbackObject, int sessionId, StringBuilder signaling)
        {
            return 0;
        }

        public int onSendMessageFailure(int callbackIndex, int callbackObject, int sessionId, int messageId, string reason, int code)
        {
            return 0;
        }

        public int onSendMessageSuccess(int callbackIndex, int callbackObject, int sessionId, int messageId)
        {
            return 0;
        }

        public int onSendOutOfDialogMessageFailure(int callbackIndex, int callbackObject, int messageId, string fromDisplayName, string from, string toDisplayName, string to, string reason, int code)
        {
            return 0;
        }

        public int onSendOutOfDialogMessageSuccess(int callbackIndex, int callbackObject, int messageId, string fromDisplayName, string from, string toDisplayName, string to)
        {
            return 0;
        }

        public int onTransferRinging(int callbackIndex, int callbackObject, int sessionId)
        {
            return 0;
        }

        public int onTransferTrying(int callbackIndex, int callbackObject, int sessionId)
        {
            return 0;
        }

        public int onVideoDecoderCallback(IntPtr callbackObject, int sessionId, int width, int height, int framerate, int bitrate)
        {
            return 0;
        }

        public int onVideoRawCallback(IntPtr callbackObject, int sessionId, int callbackType, int width, int height, byte[] data, int dataLength)
        {
            return 0;
        }

        public int onWaitingFaxMessage(int callbackIndex, int callbackObject, string messageAccount, int urgentNewMessageCount, int urgentOldMessageCount, int newMessageCount, int oldMessageCount)
        {
            return 0;
        }

        public int onWaitingVoiceMessage(int callbackIndex, int callbackObject, string messageAccount, int urgentNewMessageCount, int urgentOldMessageCount, int newMessageCount, int oldMessageCount)
        {
            return 0;
        }
        #endregion

        void Init()
        {
            if (_inited) return;
            _shouldBeRegistered = true;
            _sdkLib = new PortSIPLib(0, 0, this);

            //
            // Create and set the SIP callback handers, this MUST called before
            // _sdkLib.initialize();
            //
            _sdkLib.createCallbackHandlers();

            //var logFilePath = Host.GetPluginDirectory("Telephone");
            var rd = new Random();
            var portno = rd.Next(10000, 11000);
            // Initialize the SDK
            /*
             * TRANSPORT_TYPE transportType,
                                          String localIp,
                                          Int32 localSIPPort, 
                                          PORTSIP_LOG_LEVEL logLevel,
                                          String logFilePath,
                                          Int32 maxCallSessions,
                                          String sipAgentString,
                                          Int32 audioDeviceLayer,
                                          Int32 videoDeviceLayer
                                          */
            var tt = TRANSPORT_TYPE.TRANSPORT_UDP;
            //switch (tt)
            //{
            //    case SipTransportType.Tcp:
            //        tt = TRANSPORT_TYPE.TRANSPORT_TCP;
            //        break;
            //    case SipTransportType.Tls:
            //        tt = TRANSPORT_TYPE.TRANSPORT_TLS;
            //        break;
            //}

            int rt = _sdkLib.initialize(tt,
                "0.0.0.0",
                portno,
                PORTSIP_LOG_LEVEL.PORTSIP_LOG_NONE,
                "",
                1,
                "PortSIP VoIP SDK 15.0",
                0,
                0);
            
            if (rt != 0)
            {
                _sdkLib.releaseCallbackHandlers();
                return;
            }
            rt = _sdkLib.setLicenseKey("1WINoR00RTg2NUQzM0I4OEJFQUQ1MzZGOTZFOUI5REMxMTE2OUBGQkFCNDNCQUEyRTFEOUQyNDI1RERDNzAzQkI3MjU5QkA4MjY2MjRERjAwNUJGMUEyRDYzNDNCQjNDNDExNURFNkA0NDREMzUyNENCRDY5QTJCOEIxNzAwMEI1MTYyQjc0MQ");
            if (rt == PortSIP_Errors.ECoreTrialVersionLicenseKey)
            {
                _logger.Warn(this, "This sample was built base on evaluation PortSIP VoIP SDK, which allows only three minutes conversation. The conversation will be cut off automatically after three minutes, then you can't hearing anything. Feel free contact us at: sales@portsip.com to purchase the official version.");
            }
            else if (rt == PortSIP_Errors.ECoreWrongLicenseKey)
            {
                _logger.Warn(this, "The wrong license key was detected, please check with sales@portsip.com or support@portsip.com");
            }
            _sdkLib.enableAEC(EC_MODES.EC_DEFAULT);
            _sdkLib.enableVAD(true);
            _sdkLib.enableAGC(AGC_MODES.AGC_NONE);
            _sdkLib.enableANS(NS_MODES.NS_DEFAULT);
            foreach (AUDIOCODEC_TYPE val in Enum.GetValues(typeof(AUDIOCODEC_TYPE)))
            {
                _sdkLib.addAudioCodec(val);
            }


            /*
             * String userName,
                         String displayName,
                         String authName,
                         String password,
                         String sipDomain,
                         String sipServerAddr,
                         Int32 sipServerPort,
                         String stunServerAddr,
                         Int32 stunServerPort,
                         String outboundServerAddr,
                         Int32 outboundServerPort*/
            rt = _sdkLib.setUser(
                Account.Username,
                Account.Username,
                Account.Username,
                Account.Password,
                "",
                Account.Server,
                5060,
                null,
                -1,
                Account.Server,
                5060);
            if (rt != 0)
            {
                _logger.Error(this, "Telephone registration failed");
                _sdkLib.unInitialize();
                _sdkLib.releaseCallbackHandlers();
                return;
            }
            _sdkLib.setSrtpPolicy(SRTP_POLICY.SRTP_POLICY_NONE);
            _sdkLib.setDoNotDisturb(false);
            _inited = true;
            _logger.Info(this, "Registration successful");
        }
        public override async Task Register()
        {
            var source = new CancellationTokenSource(10000);
            Init();
            await Task.Run(() =>
            {
                var rt = _sdkLib.registerServer(120, 0);
            }, source.Token);
            source.Dispose();
        }

        public override async Task Reject()
        {
            IsInCall = false;
            IsBusy = false;
            IsIncoming = false;
            var source = new CancellationTokenSource(10000);
            await Task.Run(() =>
            {
                _sdkLib.rejectCall(_sessionId, 486);
            }, source.Token);

            OnRejected(CurrentNumber);
        }

        public override async Task StartDtmf(string k)
        {
            int i = -1;
            if (!int.TryParse(k, out i))
            {
                if (k == "*") i = 10;
                else i = 11;
            }
            var source = new CancellationTokenSource(10000);
            await Task.Run(() =>
            {
                _sdkLib.sendDtmf(_sessionId, DTMF_METHOD.DTMF_RFC2833, i, 160, true);
            }, source.Token);

        }

        public override Task StopDtmf(string k)
        {
            return Task.CompletedTask;
        }

        public override void Unmute()
        {
            if (!_inited) return;
            _sdkLib.muteMicrophone(false);
        }

        public override async Task Unregister()
        {
            if (!_inited) return;
            _shouldBeRegistered = false;
            var source = new CancellationTokenSource(10000);
            await Task.Run(() =>
            {
                _sdkLib.unRegisterServer();
                _sdkLib.unInitialize();
                _sdkLib.releaseCallbackHandlers();
            }, source.Token);
        }



        public int onDialogStateUpdated(int callbackIndex, int callbackObject, string BLFMonitoredUri, string BLFDialogState, string BLFDialogId, string BLFDialogDirection)
        {
            return 0;
        }
    }
}
