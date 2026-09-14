using System;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SecureOverlay.Platform.Windows;
using System.Windows;

namespace SecureOverlay.Services
{
    public class VoiceInputService : IDisposable
    {
        private static readonly int[] CloudRecoveryCooldownMinutes = { 5, 10, 15 };

        private WebView2? _webView;
        private System.Windows.Controls.Grid? _hostContainer;
        private bool _isListening = false;
        private bool _isInitialized = false;
        private bool _isInitializing = false;
        private bool _permissionGranted = false;
        private bool _isDisposed = false;
        private EventHandler<CoreWebView2PermissionRequestedEventArgs>? _permissionRequestedHandler;
        private readonly Func<byte[], CancellationToken, Task<string>>? _cloudTranscriber;
        private readonly Func<CancellationToken, Task>? _cloudProbe;
        private readonly bool _fallbackToNative;
        private readonly bool _preferCloud;
        private readonly SemaphoreSlim _audioGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _disposeCancellation = new CancellationTokenSource();
        private bool _useCloud;
        private bool _fallbackStarted;
        private int _cloudRecoveryCooldownIndex;
        private DateTime? _nextCloudRecoveryProbeUtc;
        private bool _probeCloudOnNextStart;
        private bool _pendingCloudRecovery;
        private bool _immediateCloudProbePending;
        private bool _immediateCloudProbeConsumed;
        private int _inflightCloudJobs;
        private bool _stopRequested;
        private bool _captureCompletedRaised;
        private bool _cloudFlushReceived;

        public event EventHandler<string>? SpeechRecognized;
        public event EventHandler<string>? SpeechHypothesis;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler? CaptureCompleted;

        public VoiceInputService(
            Func<byte[], CancellationToken, Task<string>>? cloudTranscriber = null,
            bool fallbackToNative = true,
            Func<CancellationToken, Task>? cloudProbe = null)
        {
            _cloudTranscriber = cloudTranscriber;
            _cloudProbe = cloudProbe;
            _preferCloud = cloudTranscriber != null;
            _useCloud = cloudTranscriber != null;
            _fallbackToNative = fallbackToNative;
            Log.WriteLine("VoiceInputService constructor");
        }

        public async Task<bool> InitializeAsync()
        {
            if (_isDisposed)
            {
                Log.WriteLine("Cannot initialize disposed voice service");
                return false;
            }

            if (_isInitialized)
            {
                Log.WriteLine("Already initialized");
                return true;
            }

            if (_isInitializing)
            {
                Log.WriteLine("Already initializing");
                return false;
            }

            _isInitializing = true;

            try
            {
                Log.WriteLine("════════════════════════════════════════════════");
                Log.WriteLine("Starting WebView2 initialization...");
                
                var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        Log.WriteLine("Step 1: Creating WebView2 instance...");
                        StatusChanged?.Invoke(this, "Creating browser...");
                        
                        _webView = new WebView2
                        {
                            Width = 1,
                            Height = 1,
                            Visibility = Visibility.Collapsed
                        };
                        
                        Log.WriteLine("Step 2: Adding WebView2 to visual tree...");
                        
                        var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                        if (mainWindow != null)
                        {
                            var container = mainWindow.FindName("WebView2Container") as System.Windows.Controls.Grid;
                            if (container != null)
                            {
                                _hostContainer = container;
                                container.Children.Add(_webView);
                                Log.WriteLine("  ✓ WebView2 added to MainWindow container");
                            }
                            else
                            {
                                throw new Exception("WebView2Container not found in MainWindow!");
                            }
                        }
                        else
                        {
                            throw new Exception("MainWindow not found!");
                        }
                        
                        Log.WriteLine("Step 3: Creating environment...");
                        StatusChanged?.Invoke(this, "Setting up environment...");
                        
                        var userDataFolder = WindowsAppPaths.WebView2CachePath;
                        
                        Log.WriteLine($"  User data folder: {userDataFolder}");
                        
                        var env = await CoreWebView2Environment.CreateAsync(
                            browserExecutableFolder: null,
                            userDataFolder: userDataFolder
                        );
                        
                        Log.WriteLine("  ✓ Environment created successfully");
                        
                        Log.WriteLine("Step 4: Initializing CoreWebView2...");
                        StatusChanged?.Invoke(this, "Initializing browser core...");
                        
                        await _webView.EnsureCoreWebView2Async(env);
                        
                        Log.WriteLine("  ✓✓✓ CoreWebView2 initialized successfully!");
                        
                        Log.WriteLine("Step 5: Configuring permissions...");

                        _permissionRequestedHandler = OnPermissionRequested;
                        _webView.CoreWebView2.PermissionRequested += _permissionRequestedHandler;

                        Log.WriteLine("  ✓ Permission handler configured");

                        Log.WriteLine("Step 6: Setting up message handler...");
                        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                        Log.WriteLine("Step 7: Loading speech recognition HTML...");
                        StatusChanged?.Invoke(this, "Loading speech engine...");

                        var tempFolder = WindowsAppPaths.TempRoot;

                        if (!System.IO.Directory.Exists(tempFolder))
                        {
                            System.IO.Directory.CreateDirectory(tempFolder);
                        }

                        var htmlFilePath = WindowsAppPaths.SpeechRecognitionHtmlPath;
                        System.IO.File.WriteAllText(htmlFilePath, _useCloud ? GetCloudCaptureHTML() : GetSpeechRecognitionHTML());

                        Log.WriteLine($"  HTML saved to: {htmlFilePath}");

                        _webView.CoreWebView2.Navigate($"file:///{htmlFilePath.Replace("\\", "/")}");
                        
                        Log.WriteLine("Step 8: Waiting for page load...");
                        
                        var tcs = new TaskCompletionSource<bool>();
                        
                        void navigationCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e)
                        {
                            Log.WriteLine($"  Navigation completed. Success: {e.IsSuccess}");
                            if (e.IsSuccess)
                            {
                                tcs.TrySetResult(true);
                            }
                            else
                            {
                                Log.WriteLine($"  ✗ Navigation failed: {e.WebErrorStatus}");
                                tcs.TrySetResult(false);
                            }
                        }
                        
                        _webView.CoreWebView2.NavigationCompleted += navigationCompleted;
                        
                        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(10000));
                        
                        _webView.CoreWebView2.NavigationCompleted -= navigationCompleted;
                        
                        if (completedTask == tcs.Task && await tcs.Task)
                        {
                            Log.WriteLine("  ✓ Page loaded successfully");
                            await Task.Delay(500);
                            
                            try
                            {
                                var permResult = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                                    navigator.permissions.query({name:'microphone'}).then(r => r.state);
                                ");
                                Log.WriteLine($"  Microphone permission status: {permResult}");
                                _permissionGranted = permResult.Contains("granted");
                            }
                            catch
                            {
                                _permissionGranted = false;
                            }
                            
                            Log.WriteLine("════════════════════════════════════════════════");
                            Log.WriteLine("✓✓✓ VOICE RECOGNITION READY! ✓✓✓");
                            if (_permissionGranted)
                            {
                                Log.WriteLine("✓✓✓ MICROPHONE PERMISSION ALREADY GRANTED! ✓✓✓");
                            }
                            else
                            {
                                Log.WriteLine("⚠️ Microphone permission needed - will prompt on first use");
                            }
                            Log.WriteLine("════════════════════════════════════════════════");
                            
                            StatusChanged?.Invoke(this, "Ready");
                            return true;
                        }
                        else
                        {
                            throw new Exception("Navigation timeout");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"✗ Inner initialization error: {ex.GetType().Name}");
                        Log.WriteLine($"  Error type: {ex.GetType().Name}");
                        return false;
                    }
                });

                _isInitialized = await result;
                _isInitializing = false;
                
                return _isInitialized;
            }
            catch (Exception ex)
            {
                Log.WriteLine("════════════════════════════════════════════════");
                Log.WriteLine("✗✗✗ VOICE INITIALIZATION FAILED ✗✗✗");
                Log.WriteLine($"Error type: {ex.GetType().Name}");
                Log.WriteLine("════════════════════════════════════════════════");
                
                _isInitializing = false;
                _isInitialized = false;
                
                StatusChanged?.Invoke(this, $"Error: {ex.Message}");
                return false;
            }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var message = e.TryGetWebMessageAsString();
                Log.WriteLine($"Voice browser event length_bucket={LengthBucket(message.Length)}");
                
                if (message.StartsWith("TRANSCRIPT:"))
                {
                    var text = message.Substring("TRANSCRIPT:".Length);
                    var route = _fallbackStarted ? "native_fallback" : "native";
                    Log.WriteLine($"Speech transcription succeeded route={route} transcript_length_bucket={LengthBucket(text.Length)}");
                    SpeechRecognized?.Invoke(this, text);
                    StatusChanged?.Invoke(this, _fallbackStarted ? "Native fallback recognized" : "Native speech recognized");
                }
                else if (message.StartsWith("INTERIM:"))
                {
                    var text = message.Substring("INTERIM:".Length).Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        SpeechHypothesis?.Invoke(this, text);
                    }
                }
                else if (message.StartsWith("AUDIO:"))
                {
                    Interlocked.Increment(ref _inflightCloudJobs);
                    _ = ProcessCloudAudioAsync(message.Substring("AUDIO:".Length));
                }
                else if (message == "FLUSHED")
                {
                    _cloudFlushReceived = true;
                    TryRaiseCaptureCompleted();
                }
                else if (message.StartsWith("STATUS:"))
                {
                    var status = message.Substring("STATUS:".Length);
                    if (_fallbackStarted)
                    {
                        status = status.Contains("Listening", StringComparison.OrdinalIgnoreCase)
                            ? "Listening with native fallback"
                            : status == "Ready" ? "Native fallback ready" : status;
                    }
                    Log.WriteLine($"Status: {status}");
                    StatusChanged?.Invoke(this, status);
                    if (_stopRequested
                        && (string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(status, "Native fallback ready", StringComparison.OrdinalIgnoreCase)))
                    {
                        TryRaiseCaptureCompleted();
                    }
                }
                else if (message.StartsWith("ERROR:"))
                {
                    var error = message.Substring("ERROR:".Length);
                    Log.WriteLine($"Error: {error}");
                    if (_useCloud && _fallbackToNative)
                    {
                        _ = FallbackToNativeAsync();
                        return;
                    }
                    
                    if (error.Contains("not-allowed") || error.Contains("permission"))
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            ShowPermissionPrompt();
                        });
                    }
                    else
                    {
                        StatusChanged?.Invoke(this, $"Error: {error}");
                    }
                }
                else if (message.StartsWith("PERMISSION:"))
                {
                    var permission = message.Substring("PERMISSION:".Length);
                    Log.WriteLine($"📋 Permission status: {permission}");
                    _permissionGranted = (permission == "granted");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Message handling error: {ex.GetType().Name}");
            }
        }

        private static string LengthBucket(int length) => length switch
        {
            <= 0 => "empty",
            <= 40 => "1-40",
            <= 160 => "41-160",
            <= 640 => "161-640",
            _ => "641+"
        };

        private async Task ProcessCloudAudioAsync(string base64)
        {
            var acquired = false;
            try
            {
                if (!_useCloud || _cloudTranscriber == null || _fallbackStarted) return;
                await _audioGate.WaitAsync(_disposeCancellation.Token);
                acquired = true;
                if (_fallbackStarted) return;
                var pcm = Convert.FromBase64String(base64);
                var transcript = await _cloudTranscriber(pcm, _disposeCancellation.Token);
                if (!string.IsNullOrWhiteSpace(transcript))
                {
                    var text = transcript.Trim();
                    Log.WriteLine($"Speech transcription succeeded route=cloud transcript_length_bucket={LengthBucket(text.Length)}");
                    ClearCloudFallbackState();
                    SpeechRecognized?.Invoke(this, text);
                    StatusChanged?.Invoke(this, "Cloud speech recognized");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.WriteLine($"Speech transcription failed route=cloud error_code={ex.GetType().Name} fallback={_fallbackToNative}");
                StatusChanged?.Invoke(this, "Cloud speech unavailable");
                if (_fallbackToNative) await FallbackToNativeAsync();
            }
            finally
            {
                if (acquired) _audioGate.Release();
                Interlocked.Decrement(ref _inflightCloudJobs);
                TryRaiseCaptureCompleted();
            }
        }

        private async Task FallbackToNativeAsync()
        {
            if (_fallbackStarted || _webView?.CoreWebView2 == null) return;
            _fallbackStarted = true;
            _useCloud = false;
            _pendingCloudRecovery = false;
            // First failure: retry cloud on the next mic. Later failures: 5 → 10 → 15 minute cooldown.
            if (!_immediateCloudProbeConsumed)
            {
                _immediateCloudProbeConsumed = true;
                _immediateCloudProbePending = true;
                _probeCloudOnNextStart = true;
                Log.WriteLine("Speech recognition route changed route=native_fallback reason=cloud_unavailable probe=next_mic");
            }
            else
            {
                ScheduleCloudRecoveryProbe();
            }
            StatusChanged?.Invoke(this, "Using native speech fallback");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var htmlFilePath = WindowsAppPaths.SpeechRecognitionHtmlPath;
                System.IO.File.WriteAllText(htmlFilePath, GetSpeechRecognitionHTML());
                _webView.CoreWebView2.Navigate($"file:///{htmlFilePath.Replace("\\", "/")}");
                await Task.Delay(500);
                if (_isListening) await _webView.CoreWebView2.ExecuteScriptAsync("startListening()");
            });
        }

        private void ScheduleCloudRecoveryProbe()
        {
            if (!_preferCloud || !_fallbackToNative)
            {
                return;
            }

            _immediateCloudProbePending = false;
            _pendingCloudRecovery = false;
            var minutes = CloudRecoveryCooldownMinutes[Math.Min(_cloudRecoveryCooldownIndex, CloudRecoveryCooldownMinutes.Length - 1)];
            _nextCloudRecoveryProbeUtc = DateTime.UtcNow.AddMinutes(minutes);
            if (_cloudRecoveryCooldownIndex < CloudRecoveryCooldownMinutes.Length - 1)
            {
                _cloudRecoveryCooldownIndex++;
            }

            _probeCloudOnNextStart = true;
            Log.WriteLine($"Cloud speech recovery scheduled in {minutes} minutes");
        }

        private void ClearCloudFallbackState()
        {
            _fallbackStarted = false;
            _pendingCloudRecovery = false;
            _probeCloudOnNextStart = false;
            _immediateCloudProbePending = false;
            _immediateCloudProbeConsumed = false;
            _nextCloudRecoveryProbeUtc = null;
            _cloudRecoveryCooldownIndex = 0;
            _useCloud = _preferCloud;
        }

        /// <summary>
        /// After cloud→native fallback: on the next mic (or after cooldown), probe cloud while staying
        /// on native for this utterance. Switch back only after StopListening if the probe succeeds.
        /// Failed probes escalate cooldown: 5 → 10 → 15 minutes.
        /// </summary>
        private async Task ProbeCloudRecoveryIfDueAsync()
        {
            if (!_preferCloud
                || _cloudTranscriber == null
                || !_fallbackStarted
                || !_probeCloudOnNextStart)
            {
                return;
            }

            if (!_immediateCloudProbePending
                && _nextCloudRecoveryProbeUtc.HasValue
                && DateTime.UtcNow < _nextCloudRecoveryProbeUtc.Value)
            {
                Log.WriteLine("Cloud speech recovery still cooling down - continuing with native");
                StatusChanged?.Invoke(this, "Listening with native fallback");
                return;
            }

            Log.WriteLine(_immediateCloudProbePending
                ? "Probing cloud speech recovery immediately on next mic (stay native for utterance)"
                : "Probing cloud speech recovery after cooldown (stay native for utterance)");
            StatusChanged?.Invoke(this, "Retrying cloud speech…");
            _immediateCloudProbePending = false;
            _probeCloudOnNextStart = false;

            try
            {
                // Prefer dedicated reachability probe (bypasses local "no speech" gate).
                if (_cloudProbe != null)
                {
                    await _cloudProbe(_disposeCancellation.Token);
                }
                else if (_cloudTranscriber != null)
                {
                    _ = await _cloudTranscriber(Array.Empty<byte>(), _disposeCancellation.Token);
                }
                else
                {
                    return;
                }

                _pendingCloudRecovery = true;
                Log.WriteLine("Cloud speech probe succeeded - will switch after this native utterance");
                StatusChanged?.Invoke(this, "Cloud speech recovered — switching after this utterance");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Cloud speech probe failed error_code={ex.GetType().Name}");
                ScheduleCloudRecoveryProbe();
                var minutes = CloudRecoveryCooldownMinutes[Math.Min(Math.Max(0, _cloudRecoveryCooldownIndex - 1), CloudRecoveryCooldownMinutes.Length - 1)];
                StatusChanged?.Invoke(this, $"Cloud speech still unavailable — using native ({minutes}m cooldown)");
            }
        }

        private async Task ApplyPendingCloudRecoveryIfNeededAsync()
        {
            if (!_pendingCloudRecovery || !_preferCloud || _cloudTranscriber == null || _webView?.CoreWebView2 == null)
            {
                return;
            }

            Log.WriteLine("Speech recognition route changed route=cloud reason=probe_success_after_utterance");
            ClearCloudFallbackState();
            StatusChanged?.Invoke(this, "Cloud speech ready");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var htmlFilePath = WindowsAppPaths.SpeechRecognitionHtmlPath;
                System.IO.File.WriteAllText(htmlFilePath, GetCloudCaptureHTML());
                _webView.CoreWebView2.Navigate($"file:///{htmlFilePath.Replace("\\", "/")}");
                await Task.Delay(500);
            });
        }

        private static string GetCloudCaptureHTML()
        {
            return @"<!doctype html><html><body><p id='status'>Ready</p><script>
let stream, context, source, processor, listening=false, samples=[];
function sendChunk(force) {
  const size = 64000;
  while (samples.length >= size || (force && samples.length >= 8000)) {
    const count = samples.length >= size ? size : samples.length;
    const bytes = new Uint8Array(count * 2);
    for (let i=0; i<count; i++) { const value=Math.max(-1,Math.min(1,samples[i])); const sample=value<0?value*32768:value*32767; bytes[i*2]=sample&255; bytes[i*2+1]=(sample>>8)&255; }
    samples=samples.slice(count);
    let binary=''; for (let i=0;i<bytes.length;i+=8192) binary+=String.fromCharCode(...bytes.subarray(i,i+8192));
    window.chrome.webview.postMessage('AUDIO:'+btoa(binary));
  }
}
async function startListening() {
  if (listening) return;
  try {
    stream=await navigator.mediaDevices.getUserMedia({audio:{channelCount:1,echoCancellation:true,noiseSuppression:true,autoGainControl:true}});
    context=new AudioContext(); source=context.createMediaStreamSource(stream); processor=context.createScriptProcessor(4096,1,1);
    const ratio=context.sampleRate/16000;
    processor.onaudioprocess=e=>{ const input=e.inputBuffer.getChannelData(0); for(let i=0;i<input.length;i+=ratio) samples.push(input[Math.floor(i)]); sendChunk(false); };
    source.connect(processor); processor.connect(context.destination); listening=true;
    window.chrome.webview.postMessage('STATUS:Listening with cloud speech');
  } catch(e) { window.chrome.webview.postMessage('ERROR:'+e.message); }
}
function stopListening() {
  if (!listening) { window.chrome.webview.postMessage('FLUSHED'); window.chrome.webview.postMessage('STATUS:Ready'); return; } listening=false; processor?.disconnect(); source?.disconnect(); stream?.getTracks().forEach(t=>t.stop()); context?.close(); sendChunk(true);
  window.chrome.webview.postMessage('FLUSHED');
  window.chrome.webview.postMessage('STATUS:Ready');
}
window.addEventListener('load',()=>window.chrome.webview.postMessage('STATUS:Ready'));
</script></body></html>";
        }

        private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            Log.WriteLine($"  🔐 Permission requested: {e.PermissionKind}");

            if (e.PermissionKind == CoreWebView2PermissionKind.Microphone)
            {
                Log.WriteLine("  ✅ Auto-granting microphone permission (already saved)");
                e.State = CoreWebView2PermissionState.Allow;
            }
        }

        private async void ShowPermissionPrompt()
        {
            if (_webView == null) return;

            Log.WriteLine("════════════════════════════════════════════════");
            Log.WriteLine("OPENING PERMISSION WINDOW");
            Log.WriteLine("════════════════════════════════════════════════");

            try
            {
                var permissionWindow = new MicrophonePermissionWindow();
                var result = permissionWindow.ShowDialog();

                if (result == true && permissionWindow.PermissionGranted)
                {
                    Log.WriteLine("✅ User granted microphone permission!");
                    _permissionGranted = true;
                    
                    StatusChanged?.Invoke(this, "Permission granted");
                    
                    InvisibleMessageBox.Show(
                        "✅ Microphone permission granted!\n\n" +
                        "Voice input is now ready to use.\n\n" +
                        "Click the 🎤 button to start speaking!",
                        "Success"
                    );
                    
                    if (_webView?.CoreWebView2 != null)
                    {
                        await _webView.CoreWebView2.ExecuteScriptAsync("startListening()");
                        _isListening = true;
                    }
                }
                else
                {
                    Log.WriteLine("❌ User denied or cancelled permission");
                    StatusChanged?.Invoke(this, "Permission denied");
                    
                    InvisibleMessageBox.Show(
                        "❌ Microphone permission denied.\n\n" +
                        "Voice input will not work until you grant permission.\n\n" +
                        "Click the 🎤 button again to retry.",
                        "Permission Required"
                    );
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"❌ Permission window error: {ex.GetType().Name}");
            }
            finally
            {
                Log.WriteLine("════════════════════════════════════════════════");
            }
        }

        private string GetSpeechRecognitionHTML()
        {
            return @"
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset='utf-8'>
            <title>Speech Recognition</title>
            <style>
                body {
                    background: #1a1a1a;
                    color: #fff;
                    font-family: Arial, sans-serif;
                    padding: 20px;
                    margin: 0;
                }
                h1 { color: #4CAF50; }
                #status {
                    padding: 15px;
                    background: #333;
                    border-radius: 8px;
                    margin-top: 10px;
                    font-size: 14px;
                }
            </style>
        </head>
        <body>
            <h1>🎤 Speech Recognition</h1>
            <p id='status'>Ready</p>
            <script>
                console.log('Speech page loaded');
                console.log('Location:', window.location.href);
                console.log('isSecureContext:', window.isSecureContext);
                
                let recognition = null;
                let isListening = false;
                let shouldBeListening = false;

                function initSpeechRecognition() {
                    const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
                    
                    if (!SpeechRecognition) {
                        window.chrome.webview.postMessage('ERROR:Not supported');
                        return false;
                    }

                    recognition = new SpeechRecognition();
                    
                    recognition.continuous = true;
                    recognition.interimResults = true;
                    recognition.maxAlternatives = 1;
                    recognition.lang = 'en-US';

                    recognition.onstart = () => {
                        console.log('✓ Recognition started');
                        isListening = true;
                        document.getElementById('status').textContent = '🎤 Listening continuously...';
                        window.chrome.webview.postMessage('STATUS:Listening - speak now!');
                    };

                    recognition.onresult = (event) => {
                        console.log('Got speech result');
                        
                        for (let i = event.resultIndex; i < event.results.length; i++) {
                            const result = event.results[i];
                            const transcript = result[0].transcript.trim();
                            
                            if (result.isFinal) {
                                console.log('✓ Final transcript:', transcript);
                                document.getElementById('status').textContent = '🎤 Heard: ' + transcript;
                                window.chrome.webview.postMessage('TRANSCRIPT:' + transcript);
                            } else {
                                console.log('... Interim:', transcript);
                                document.getElementById('status').textContent = '🎤 ... ' + transcript;
                                window.chrome.webview.postMessage('INTERIM:' + transcript);
                            }
                        }
                    };

                    recognition.onerror = (event) => {
                        console.error('Speech error:', event.error);
                        
                        if (event.error === 'no-speech') {
                            console.log('No speech detected, continuing to listen...');
                            document.getElementById('status').textContent = '🎤 Listening... (speak now)';
                            return;
                        }
                        
                        if (event.error === 'aborted') {
                            console.log('Recognition aborted');
                        } else {
                            console.error('Error:', event.error);
                            document.getElementById('status').textContent = 'Error: ' + event.error;
                            window.chrome.webview.postMessage('ERROR:' + event.error);
                        }
                    };

                    recognition.onend = () => {
                        console.log('Recognition ended');
                        isListening = false;
                        
                        if (shouldBeListening) {
                            console.log('Auto-restarting recognition...');
                            setTimeout(() => {
                                if (shouldBeListening) {
                                    try {
                                        recognition.start();
                                        console.log('✓ Restarted');
                                    } catch (err) {
                                        console.error('Restart failed:', err);
                                    }
                                }
                            }, 100);
                        } else {
                            console.log('User stopped listening');
                            document.getElementById('status').textContent = 'Ready';
                            window.chrome.webview.postMessage('STATUS:Ready');
                        }
                    };

                    console.log('✓ Speech recognition initialized (continuous mode)');
                    window.chrome.webview.postMessage('STATUS:Ready');
                    return true;
                }

                function startListening() {
                    console.log('startListening() called');
                    
                    if (!recognition) {
                        console.log('Initializing recognition...');
                        if (!initSpeechRecognition()) {
                            return;
                        }
                    }

                    if (isListening) {
                        console.log('Already listening');
                        return;
                    }

                    shouldBeListening = true;
                    
                    try {
                        console.log('Starting continuous recognition...');
                        recognition.start();
                    } catch (err) {
                        console.error('Start error:', err);
                        
                        if (err.name === 'InvalidStateError') {
                            console.log('Already started, continuing...');
                        } else {
                            window.chrome.webview.postMessage('ERROR:' + err.message);
                        }
                    }
                }

                function stopListening() {
                    console.log('stopListening() called');
                    
                    shouldBeListening = false;
                    
                    if (!recognition || !isListening) {
                        console.log('Not listening, nothing to stop');
                        window.chrome.webview.postMessage('STATUS:Ready');
                        return;
                    }

                    try {
                        console.log('Stopping recognition...');
                        recognition.stop();
                        document.getElementById('status').textContent = 'Stopped';
                    } catch (err) {
                        console.error('Stop error:', err);
                    }
                }

                window.addEventListener('load', () => {
                    console.log('Initializing on page load...');
                    initSpeechRecognition();
                });
            </script>
        </body>
        </html>";
        }

        public async void StartListening()
        {
            Log.WriteLine("StartListening() called");
            _stopRequested = false;
            _captureCompletedRaised = false;
            _cloudFlushReceived = false;
            
            if (!_isInitialized || _webView?.CoreWebView2 == null)
            {
                Log.WriteLine("Not initialized");
                return;
            }

            if (_isListening)
            {
                Log.WriteLine("Already listening");
                return;
            }

            try
            {
                await ProbeCloudRecoveryIfDueAsync();
                await _webView.CoreWebView2.ExecuteScriptAsync("startListening()");
                _isListening = true;
                Log.WriteLine("✓ Started listening");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ Start failed: {ex.GetType().Name}");
            }
        }

        public async void StopListening()
        {
            _stopRequested = true;
            _captureCompletedRaised = false;
            if (!IsCloudMode() || !_isListening || _webView?.CoreWebView2 == null)
            {
                _cloudFlushReceived = true;
            }

            if (!_isListening || _webView?.CoreWebView2 == null)
            {
                _isListening = false;
                _ = ApplyPendingCloudRecoveryIfNeededAsync();
                TryRaiseCaptureCompleted();
                return;
            }

            try
            {
                await _webView.CoreWebView2.ExecuteScriptAsync("stopListening()");
                _isListening = false;
            }
            catch
            {
                _isListening = false;
            }

            await ApplyPendingCloudRecoveryIfNeededAsync();
        }

        private void TryRaiseCaptureCompleted()
        {
            if (!_stopRequested || _captureCompletedRaised || _isListening)
            {
                return;
            }

            if (Volatile.Read(ref _inflightCloudJobs) > 0)
            {
                return;
            }

            if (!_cloudFlushReceived)
            {
                return;
            }

            _captureCompletedRaised = true;
            _stopRequested = false;
            Log.WriteLine("Voice capture completed — transcription finished");
            CaptureCompleted?.Invoke(this, EventArgs.Empty);
        }

        public bool IsListening() => _isListening;
        public bool IsInitialized() => _isInitialized;
        public bool IsCloudMode() => _useCloud && !_fallbackStarted;
        public bool PrefersCloudMode() => _preferCloud;

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _disposeCancellation.Cancel();
            _isListening = false;
            _isInitializing = false;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (_webView?.CoreWebView2 != null)
                {
                    if (_permissionRequestedHandler != null)
                    {
                        _webView.CoreWebView2.PermissionRequested -= _permissionRequestedHandler;
                        _permissionRequestedHandler = null;
                    }

                    _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                }

                if (_webView != null)
                {
                    if (_webView.Parent is System.Windows.Controls.Panel parentPanel)
                    {
                        parentPanel.Children.Remove(_webView);
                    }
                    else if (_hostContainer != null)
                    {
                        _hostContainer.Children.Remove(_webView);
                    }

                    _webView.Dispose();
                }
            });

            _webView = null;
            _hostContainer = null;
            _isInitialized = false;
        }
    }
}
