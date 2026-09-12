package com.phantom.companion.ui.session

import android.Manifest
import android.app.Activity
import android.graphics.BitmapFactory
import android.util.Base64
import android.view.WindowManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.NoteAdd
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.filled.CameraAlt
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.MicOff
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.phantom.companion.data.local.PhoneSpeechSession
import com.phantom.companion.data.remote.SocketConnectionState
import com.phantom.companion.domain.model.ChatTurn
import com.phantom.companion.domain.model.DesktopPresenceState
import com.phantom.companion.domain.model.PendingAttachment
import com.phantom.companion.ui.components.ErrorBanner
import com.phantom.companion.ui.components.MarkdownText
import com.phantom.companion.ui.components.PhantomPrimaryButton
import com.phantom.companion.ui.components.PhantomSecondaryButton
import com.phantom.companion.ui.components.ProviderModelPickers
import com.phantom.companion.ui.components.StatusPill
import com.phantom.companion.ui.components.WarningBanner
import com.phantom.companion.ui.theme.PhantomAccent
import com.phantom.companion.ui.theme.PhantomBackground
import com.phantom.companion.ui.theme.PhantomDanger
import com.phantom.companion.ui.theme.PhantomLine
import com.phantom.companion.ui.theme.PhantomMuted
import com.phantom.companion.ui.theme.PhantomPrimary
import com.phantom.companion.ui.theme.PhantomSurface
import com.phantom.companion.ui.theme.PhantomSurfaceHigh
import com.phantom.companion.ui.theme.PhantomText
import kotlinx.coroutines.delay

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SessionScreen(
    viewModel: SessionViewModel,
    onNavigateToAccount: () -> Unit,
    onNavigateToPair: () -> Unit
) {
    val context = LocalContext.current
    val activity = context as? Activity

    val desktopPresence by viewModel.desktopPresence.collectAsState()
    val connectionState by viewModel.connectionState.collectAsState()
    val connectionError by viewModel.connectionError.collectAsState()
    val currentModel by viewModel.currentModel.collectAsState()
    val currentProvider by viewModel.currentProvider.collectAsState()
    val providers by viewModel.providers.collectAsState()
    val isVisionSupported by viewModel.isVisionSupported.collectAsState()
    val transcript by viewModel.transcript.collectAsState()
    val pendingAttachments by viewModel.pendingAttachments.collectAsState()
    val sessionError by viewModel.sessionError.collectAsState()
    val activePairing by viewModel.activePairing.collectAsState()
    val companionApiReady by viewModel.companionApiReady.collectAsState()
    val startupSnapshot by viewModel.startupSnapshot.collectAsState()

    val inputText by viewModel.inputText.collectAsState()
    val showNewTopicConfirmDialog by viewModel.showNewTopicConfirmDialog.collectAsState()
    val showVoiceSettings by viewModel.showVoiceSettings.collectAsState()
    val usePhoneMicrophone by viewModel.usePhoneMicrophone.collectAsState()
    val isMicListening by viewModel.isMicListening.collectAsState()

    var screenshotPreviewIndex by remember { mutableStateOf<Int?>(null) }
    var chatPreviewJpeg by remember { mutableStateOf<String?>(null) }
    var toastMessage by remember { mutableStateOf<String?>(null) }
    var toastIsError by remember { mutableStateOf(false) }

    val listState = rememberLazyListState()
    val relayLive = connectionState is SocketConnectionState.Connected

    val speechSession = remember(context) {
        PhoneSpeechSession(
            context = context,
            cloud = PhoneSpeechSession.CloudSpeech(
                isPreferred = { viewModel.prefersCloudSpeech() },
                transcribe = { pcm -> viewModel.transcribeCloudSpeech(pcm) }
            ),
            onPartial = { viewModel.applyPhoneDictation(it, isFinal = false) },
            onFinal = { viewModel.applyPhoneDictation(it, isFinal = true) },
            onError = { viewModel.surfaceSpeechError(it) },
            onNativeFallback = { viewModel.onNativeSpeechFallback() },
            onUtterance = { viewModel.applyPhoneDictation(it, isFinal = true, keepListening = true) }
        )
    }
    DisposableEffect(speechSession) {
        onDispose { speechSession.release() }
    }
    val micPermissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (granted) {
            viewModel.beginPhoneDictation()
            speechSession.start()
        }
    }

    // Keep screen on while thinking
    DisposableEffect(desktopPresence) {
        if (desktopPresence == DesktopPresenceState.THINKING) {
            activity?.window?.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        } else {
            activity?.window?.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }
        onDispose {
            activity?.window?.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }
    }

    // Scroll to bottom when new messages or deltas arrive
    LaunchedEffect(transcript.size, transcript.lastOrNull()?.text?.length) {
        if (transcript.isNotEmpty()) {
            listState.animateScrollToItem(transcript.lastIndex)
        }
    }

    LaunchedEffect(Unit) {
        viewModel.navigationEvent.collect { event ->
            when (event) {
                is SessionNavigationEvent.NavigateToAccount -> onNavigateToAccount()
                is SessionNavigationEvent.NavigateToPair -> onNavigateToPair()
            }
        }
    }

    LaunchedEffect(sessionError) {
        val msg = sessionError
        if (!msg.isNullOrEmpty()) {
            toastMessage = msg
            toastIsError = true
        }
    }
    LaunchedEffect(companionApiReady) {
        if (companionApiReady == false) {
            toastMessage = "Desktop companion service is not enabled on this backend yet."
            toastIsError = false
        }
    }
    LaunchedEffect(isVisionSupported) {
        if (!isVisionSupported) {
            toastMessage = "This model cannot read screens."
            toastIsError = false
        }
    }
    LaunchedEffect(pendingAttachments.size) {
        if (pendingAttachments.size >= 3) {
            toastMessage = "You can attach up to 3 screenshots. Remove one to capture again."
            toastIsError = false
        }
    }
    LaunchedEffect(toastMessage) {
        val msg = toastMessage ?: return@LaunchedEffect
        delay(2_000)
        if (toastMessage == msg) {
            toastMessage = null
            if (sessionError == msg) viewModel.clearError()
        }
    }

    Scaffold(
        containerColor = PhantomBackground,
        topBar = {
            Surface(
                color = PhantomSurfaceHigh,
                modifier = Modifier
                    .fillMaxWidth()
                    .statusBarsPadding()
                    .border(width = 1.dp, color = PhantomLine)
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 16.dp, vertical = 12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier.weight(1f)
                    ) {
                        val statusText = when {
                            companionApiReady == false -> "Companion disabled"
                            connectionError != null && connectionError!!.contains("not authorized", ignoreCase = true) -> "Auth mismatch"
                            connectionState is SocketConnectionState.Connecting -> "Connecting to relay…"
                            connectionState is SocketConnectionState.Reconnecting -> "Reconnecting…"
                            connectionState is SocketConnectionState.Failed -> "Connection error"
                            desktopPresence == DesktopPresenceState.OFFLINE -> "Desktop offline"
                            else -> null
                        }

                        StatusPill(
                            presence = desktopPresence,
                            customText = statusText
                        )
                        Spacer(modifier = Modifier.width(10.dp))

                        val email = startupSnapshot?.email.orEmpty()
                        val truncatedEmail = if (email.length > 18) email.take(15) + "…" else email
                        val tier = startupSnapshot?.accessTier?.replaceFirstChar { it.uppercase() } ?: "Free"
                        val providerLabel = currentProvider?.ifBlank { null }
                        val modelLabel = currentModel?.ifBlank { null }
                        val runtime = listOfNotNull(providerLabel, modelLabel).joinToString(" / ").ifBlank { "Phantom AI" }

                        Text(
                            text = if (email.isNotEmpty()) "$truncatedEmail · $tier · $runtime" else runtime,
                            fontSize = 12.sp,
                            color = PhantomMuted,
                            fontFamily = FontFamily.SansSerif,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis
                        )
                    }

                    IconButton(
                        onClick = viewModel::showVoiceSettings,
                        modifier = Modifier
                            .size(36.dp)
                            .testTag("button_session_voice_settings")
                    ) {
                        Icon(
                            imageVector = Icons.Default.Settings,
                            contentDescription = "Settings",
                            tint = PhantomMuted
                        )
                    }

                    IconButton(
                        onClick = onNavigateToAccount,
                        modifier = Modifier
                            .size(36.dp)
                            .testTag("button_session_account")
                    ) {
                        Icon(
                            imageVector = Icons.Default.AccountCircle,
                            contentDescription = "Account details",
                            tint = PhantomMuted
                        )
                    }
                }
            }
        },
        bottomBar = {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .background(PhantomSurfaceHigh)
                    .border(width = 1.dp, color = PhantomLine)
                    .navigationBarsPadding()
                    .padding(16.dp)
                    .imePadding()
            ) {
                val busy = desktopPresence == DesktopPresenceState.THINKING ||
                        desktopPresence == DesktopPresenceState.CAPTURING
                val atAttachmentLimit = pendingAttachments.size >= 3
                val canCapture = companionApiReady != false &&
                        activePairing != null &&
                        relayLive &&
                        isVisionSupported &&
                        !busy &&
                        !atAttachmentLimit

                if (pendingAttachments.isNotEmpty()) {
                    PendingAttachmentTray(
                        attachments = pendingAttachments,
                        onRemove = viewModel::onRemoveAttachment,
                        onClear = viewModel::onClearAttachments,
                        onOpen = { screenshotPreviewIndex = it }
                    )
                    Spacer(modifier = Modifier.height(12.dp))
                }

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.Bottom
                ) {
                    OutlinedTextField(
                        value = inputText,
                        onValueChange = viewModel::onInputTextChanged,
                        placeholder = {
                            Text(
                                text = "Ask a follow-up…",
                                color = PhantomMuted.copy(alpha = 0.6f),
                                fontSize = 14.sp
                            )
                        },
                        modifier = Modifier
                            .weight(1f)
                            .heightIn(min = 52.dp, max = 140.dp)
                            .testTag("input_follow_up"),
                        singleLine = false,
                        minLines = 1,
                        maxLines = 5,
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send),
                        keyboardActions = KeyboardActions(onSend = { viewModel.onSendFollowUpClicked() }),
                        shape = RoundedCornerShape(12.dp),
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedTextColor = PhantomText,
                            unfocusedTextColor = PhantomText,
                            focusedContainerColor = PhantomSurface,
                            unfocusedContainerColor = PhantomSurface,
                            focusedBorderColor = PhantomPrimary,
                            unfocusedBorderColor = PhantomLine
                        )
                    )

                    Spacer(modifier = Modifier.width(8.dp))

                    IconButton(
                        onClick = viewModel::showNewTopicDialog,
                        enabled = activePairing != null && relayLive,
                        modifier = Modifier
                            .size(48.dp)
                            .clip(RoundedCornerShape(12.dp))
                            .background(PhantomSurface)
                            .testTag("button_new_topic")
                    ) {
                        Icon(
                            imageVector = Icons.AutoMirrored.Filled.NoteAdd,
                            contentDescription = "New topic",
                            tint = PhantomText
                        )
                    }
                }

                Spacer(modifier = Modifier.height(12.dp))

                val canSend = (inputText.isNotBlank() || pendingAttachments.isNotEmpty()) &&
                        desktopPresence != DesktopPresenceState.THINKING &&
                        activePairing != null &&
                        relayLive
                val canMic = activePairing != null && (usePhoneMicrophone || relayLive)

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    SessionActionButton(
                        icon = Icons.Default.CameraAlt,
                        contentDescription = if (desktopPresence == DesktopPresenceState.CAPTURING) {
                            "Capturing"
                        } else {
                            "Capture screenshot"
                        },
                        onClick = viewModel::onCaptureClicked,
                        enabled = canCapture,
                        isLoading = desktopPresence == DesktopPresenceState.CAPTURING,
                        testTag = "button_capture_only",
                        modifier = Modifier.weight(1f)
                    )
                    SessionActionButton(
                        icon = if (isMicListening) Icons.Default.MicOff else Icons.Default.Mic,
                        contentDescription = if (isMicListening) "Stop listening" else "Start voice input",
                        onClick = {
                            if (usePhoneMicrophone) {
                                if (isMicListening) {
                                    val flushing = speechSession.stop()
                                    if (!flushing) viewModel.setMicListening(false)
                                } else {
                                    micPermissionLauncher.launch(Manifest.permission.RECORD_AUDIO)
                                }
                            } else if (isMicListening) {
                                viewModel.stopDesktopVoice()
                                viewModel.setMicListening(false)
                            } else if (relayLive && activePairing != null) {
                                viewModel.startDesktopVoice()
                                viewModel.setMicListening(true)
                            }
                        },
                        enabled = canMic,
                        highlighted = isMicListening,
                        highlightColor = PhantomDanger,
                        testTag = "button_mic",
                        modifier = Modifier.weight(1f)
                    )
                    SessionActionButton(
                        icon = Icons.AutoMirrored.Filled.Send,
                        contentDescription = "Send",
                        onClick = viewModel::onSendFollowUpClicked,
                        enabled = canSend,
                        highlighted = canSend,
                        highlightColor = PhantomPrimary,
                        testTag = "button_send_follow_up",
                        modifier = Modifier.weight(1f)
                    )
                }
            }
        }
    ) { innerPadding ->
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding)
        ) {
            Column(modifier = Modifier.fillMaxSize()) {
            if (transcript.isEmpty()) {
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(24.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        if (connectionError != null) {
                            Text(
                                text = "Connection Issue",
                                fontSize = 16.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = PhantomDanger
                            )
                            Spacer(modifier = Modifier.height(6.dp))
                            Text(
                                text = connectionError ?: "Unable to connect to relay server.",
                                fontSize = 13.sp,
                                color = PhantomMuted,
                                textAlign = androidx.compose.ui.text.style.TextAlign.Center
                            )
                            Spacer(modifier = Modifier.height(16.dp))
                            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                                PhantomSecondaryButton(
                                    text = "Retry",
                                    onClick = { viewModel.retryConnection() },
                                    testTag = "button_retry_connection"
                                )
                                PhantomSecondaryButton(
                                    text = "Re-pair",
                                    onClick = { viewModel.unpairAndNavigate() },
                                    testTag = "button_repair"
                                )
                            }
                        } else if (activePairing == null) {
                            Text(
                                text = "No paired desktop",
                                fontSize = 16.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = PhantomText
                            )
                            Spacer(modifier = Modifier.height(6.dp))
                            Text(
                                text = "Pair with your computer to start sending commands.",
                                fontSize = 13.sp,
                                color = PhantomMuted,
                                textAlign = androidx.compose.ui.text.style.TextAlign.Center
                            )
                            Spacer(modifier = Modifier.height(16.dp))
                            PhantomSecondaryButton(
                                text = "Pair now",
                                onClick = onNavigateToPair,
                                testTag = "button_empty_pair"
                            )
                        } else if (desktopPresence == DesktopPresenceState.OFFLINE || desktopPresence == DesktopPresenceState.CONNECTING) {
                            val desktopLabel = activePairing?.desktopDeviceLabel?.ifEmpty { "your computer" } ?: "your computer"
                            Text(
                                text = if (connectionState is SocketConnectionState.Connected) {
                                    "Waiting for desktop…"
                                } else {
                                    "Connecting to relay…"
                                },
                                fontSize = 16.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = PhantomText
                            )
                            Spacer(modifier = Modifier.height(6.dp))
                            Text(
                                text = if (connectionState is SocketConnectionState.Connected) {
                                    "Connected to relay service.\nMake sure Phantom is running on $desktopLabel."
                                } else {
                                    "Connecting to companion service…"
                                },
                                fontSize = 13.sp,
                                color = PhantomMuted,
                                textAlign = androidx.compose.ui.text.style.TextAlign.Center
                            )
                            Spacer(modifier = Modifier.height(16.dp))
                            PhantomSecondaryButton(
                                text = "Unpair desktop",
                                onClick = { viewModel.unpairAndNavigate() },
                                testTag = "button_unpair_desktop"
                            )
                        } else {
                            Text(
                                text = "Desktop is ready. Capture a screen or send a follow-up.",
                                fontSize = 16.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = PhantomText
                            )
                            Spacer(modifier = Modifier.height(6.dp))
                            Text(
                                text = "Your desktop screen will be captured and analyzed instantly.",
                                fontSize = 13.sp,
                                color = PhantomMuted,
                                textAlign = androidx.compose.ui.text.style.TextAlign.Center
                            )
                        }
                    }
                }
            } else {
                LazyColumn(
                    state = listState,
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(horizontal = 16.dp, vertical = 8.dp),
                    verticalArrangement = Arrangement.spacedBy(14.dp)
                ) {
                    items(transcript) { turn ->
                        ChatTurnBubble(
                            turn = turn,
                            onThumbnailClick = { jpeg -> chatPreviewJpeg = jpeg }
                        )
                    }
                }
            }
            }

            toastMessage?.let { message ->
                Box(
                    modifier = Modifier
                        .align(Alignment.TopCenter)
                        .padding(16.dp)
                ) {
                    if (toastIsError) {
                        ErrorBanner(message = message)
                    } else {
                        WarningBanner(message = message)
                    }
                }
            }
        }
    }

    // New Topic Confirm Dialog
    if (showNewTopicConfirmDialog) {
        AlertDialog(
            onDismissRequest = viewModel::dismissNewTopicDialog,
            containerColor = PhantomSurface,
            title = {
                Text(
                    text = "Start new topic?",
                    color = PhantomText,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.SemiBold
                )
            },
            text = {
                Text(
                    text = "This will clear the current transcript and start a fresh context with your desktop client.",
                    color = PhantomMuted,
                    fontSize = 14.sp
                )
            },
            confirmButton = {
                PhantomPrimaryButton(
                    text = "Start new topic",
                    onClick = viewModel::confirmNewTopic,
                    modifier = Modifier.width(160.dp),
                    testTag = "button_confirm_new_topic"
                )
            },
            dismissButton = {
                TextButton(
                    onClick = viewModel::dismissNewTopicDialog,
                    modifier = Modifier.testTag("button_cancel_new_topic")
                ) {
                    Text("Cancel", color = PhantomMuted)
                }
            }
        )
    }

    if (showVoiceSettings) {
        AlertDialog(
            onDismissRequest = viewModel::dismissVoiceSettings,
            containerColor = PhantomSurface,
            title = {
                Text(
                    text = "Settings",
                    color = PhantomText,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.SemiBold
                )
            },
            text = {
                Column {
                    ProviderModelPickers(
                        providers = providers,
                        selectedProviderId = currentProvider.orEmpty(),
                        selectedModelId = currentModel.orEmpty(),
                        onSelect = viewModel::onSelectRuntime,
                        enabled = relayLive && activePairing != null
                    )
                    Spacer(modifier = Modifier.height(20.dp))
                    Text(
                        text = "Voice input",
                        color = PhantomText,
                        fontSize = 15.sp,
                        fontWeight = FontWeight.SemiBold
                    )
                    Spacer(modifier = Modifier.height(8.dp))
                    Text(
                        text = "Use this phone’s microphone instead of the desktop microphone. Premium accounts try Phantom cloud speech first, then on-device recognition if that fails.",
                        color = PhantomMuted,
                        fontSize = 14.sp
                    )
                    Spacer(modifier = Modifier.height(16.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        Text(
                            text = "Phone microphone",
                            color = PhantomText,
                            fontSize = 15.sp,
                            fontWeight = FontWeight.Medium
                        )
                        Switch(
                            checked = usePhoneMicrophone,
                            onCheckedChange = { enabled ->
                                if (!enabled && isMicListening) {
                                    speechSession.stop()
                                    viewModel.stopDesktopVoice()
                                    viewModel.setMicListening(false)
                                }
                                viewModel.setUsePhoneMicrophone(enabled)
                            },
                            colors = SwitchDefaults.colors(
                                checkedTrackColor = PhantomPrimary
                            )
                        )
                    }
                }
            },
            confirmButton = {
                TextButton(
                    onClick = viewModel::dismissVoiceSettings,
                    modifier = Modifier.testTag("button_close_voice_settings")
                ) {
                    Text("Done", color = PhantomPrimary)
                }
            }
        )
    }

    val trayPreviewIndex = screenshotPreviewIndex
    if (trayPreviewIndex != null && trayPreviewIndex in pendingAttachments.indices) {
        ScreenshotPreviewDialog(
            jpegBase64 = pendingAttachments[trayPreviewIndex].thumbnailJpegBase64,
            title = "Screenshot ${trayPreviewIndex + 1} of ${pendingAttachments.size}",
            onClose = { screenshotPreviewIndex = null },
            onRemove = {
                viewModel.onRemoveAttachment(trayPreviewIndex)
                screenshotPreviewIndex = null
            }
        )
    }
    chatPreviewJpeg?.let { jpeg ->
        ScreenshotPreviewDialog(
            jpegBase64 = jpeg,
            title = "Screenshot",
            onClose = { chatPreviewJpeg = null }
        )
    }
}

@Composable
private fun SessionActionButton(
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    contentDescription: String,
    onClick: () -> Unit,
    enabled: Boolean,
    testTag: String,
    modifier: Modifier = Modifier,
    highlighted: Boolean = false,
    highlightColor: Color = PhantomPrimary,
    isLoading: Boolean = false
) {
    val active = enabled && !isLoading
    Box(
        modifier = modifier
            .height(56.dp)
            .clip(RoundedCornerShape(14.dp))
            .background(
                when {
                    isLoading || highlighted -> highlightColor
                    active -> PhantomSurface
                    else -> PhantomSurface.copy(alpha = 0.55f)
                }
            )
            .border(1.dp, PhantomLine, RoundedCornerShape(14.dp))
            .clickable(enabled = active, onClick = onClick)
            .testTag(testTag),
        contentAlignment = Alignment.Center
    ) {
        if (isLoading) {
            CircularProgressIndicator(
                modifier = Modifier.size(22.dp),
                color = Color.White,
                strokeWidth = 2.dp
            )
        } else {
            Icon(
                imageVector = icon,
                contentDescription = contentDescription,
                tint = when {
                    highlighted -> Color.White
                    active -> PhantomText
                    else -> PhantomMuted
                },
                modifier = Modifier.size(26.dp)
            )
        }
    }
}

@Composable
private fun PendingAttachmentTray(
    attachments: List<PendingAttachment>,
    onRemove: (Int) -> Unit,
    onClear: () -> Unit,
    onOpen: (Int) -> Unit
) {
    Column(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Text(
                text = "Screenshots ${attachments.size}/3",
                fontSize = 12.sp,
                color = PhantomMuted,
                fontWeight = FontWeight.Medium
            )
            TextButton(
                onClick = onClear,
                modifier = Modifier.testTag("button_clear_attachments")
            ) {
                Text("Clear", fontSize = 12.sp, color = PhantomMuted)
            }
        }
        LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            items(attachments, key = { it.index }) { attachment ->
                val bitmap = remember(attachment.thumbnailJpegBase64) {
                    decodeJpegBase64(attachment.thumbnailJpegBase64)
                }
                Box(
                    modifier = Modifier
                        .size(72.dp)
                        .clip(RoundedCornerShape(10.dp))
                        .background(PhantomSurface)
                        .border(1.dp, PhantomLine, RoundedCornerShape(10.dp))
                        .clickable { onOpen(attachment.index) }
                        .testTag("attachment_thumb_${attachment.index}")
                ) {
                    if (bitmap != null) {
                        Image(
                            bitmap = bitmap,
                            contentDescription = "Attached screenshot ${attachment.index + 1}",
                            contentScale = ContentScale.Crop,
                            modifier = Modifier.fillMaxSize()
                        )
                    }
                    IconButton(
                        onClick = { onRemove(attachment.index) },
                        modifier = Modifier
                            .align(Alignment.TopEnd)
                            .size(24.dp)
                            .testTag("button_remove_attachment_${attachment.index}")
                    ) {
                        Icon(
                            imageVector = Icons.Default.Close,
                            contentDescription = "Remove screenshot",
                            tint = Color.White,
                            modifier = Modifier
                                .size(16.dp)
                                .clip(RoundedCornerShape(8.dp))
                                .background(PhantomDanger.copy(alpha = 0.85f))
                        )
                    }
                }
            }
        }
    }
}

private fun decodeJpegBase64(value: String?): androidx.compose.ui.graphics.ImageBitmap? {
    if (value.isNullOrBlank()) return null
    return try {
        val bytes = Base64.decode(value, Base64.DEFAULT)
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
    } catch (_: Exception) {
        null
    }
}

@Composable
private fun ScreenshotPreviewDialog(
    jpegBase64: String?,
    title: String,
    onClose: () -> Unit,
    onRemove: (() -> Unit)? = null
) {
    val bitmap = remember(jpegBase64) { decodeJpegBase64(jpegBase64) }
    Dialog(
        onDismissRequest = onClose,
        properties = DialogProperties(usePlatformDefaultWidth = false)
    ) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(Color.Black.copy(alpha = 0.94f))
                .statusBarsPadding()
                .navigationBarsPadding()
                .testTag("screenshot_preview")
        ) {
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(16.dp)
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = title,
                        color = Color.White,
                        fontSize = 16.sp,
                        fontWeight = FontWeight.SemiBold,
                        modifier = Modifier.weight(1f)
                    )
                    if (onRemove != null) {
                        TextButton(
                            onClick = onRemove,
                            modifier = Modifier.testTag("button_preview_remove")
                        ) {
                            Text("Remove", color = PhantomDanger)
                        }
                    }
                    IconButton(
                        onClick = onClose,
                        modifier = Modifier.testTag("button_preview_close")
                    ) {
                        Icon(
                            imageVector = Icons.Default.Close,
                            contentDescription = "Close screenshot preview",
                            tint = Color.White
                        )
                    }
                }
                Spacer(modifier = Modifier.height(12.dp))
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .fillMaxWidth(),
                    contentAlignment = Alignment.Center
                ) {
                    if (bitmap != null) {
                        Image(
                            bitmap = bitmap,
                            contentDescription = title,
                            contentScale = ContentScale.Fit,
                            modifier = Modifier
                                .fillMaxSize()
                                .clip(RoundedCornerShape(12.dp))
                        )
                    } else {
                        Text("Preview unavailable", color = PhantomMuted)
                    }
                }
            }
        }
    }
}

@Composable
fun ChatTurnBubble(
    turn: ChatTurn,
    onThumbnailClick: (String) -> Unit = {}
) {
    val isUser = turn.role == "user"

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = if (isUser) Arrangement.End else Arrangement.Start
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth(if (isUser) 0.92f else 1f)
                .clip(
                    RoundedCornerShape(
                        topStart = 16.dp,
                        topEnd = 16.dp,
                        bottomStart = if (isUser) 16.dp else 4.dp,
                        bottomEnd = if (isUser) 4.dp else 16.dp
                    )
                )
                .background(if (isUser) PhantomSurface else PhantomSurfaceHigh)
                .border(
                    width = 1.dp,
                    color = if (turn.isError) PhantomDanger else PhantomLine,
                    shape = RoundedCornerShape(16.dp)
                )
                .padding(14.dp)
        ) {
            // Optional thumbnail if captured with this turn
            if (!turn.thumbnailBase64.isNullOrEmpty()) {
                val bitmap = remember(turn.thumbnailBase64) {
                    decodeJpegBase64(turn.thumbnailBase64)
                }
                if (bitmap != null) {
                    Image(
                        bitmap = bitmap,
                        contentDescription = "Screen capture thumbnail",
                        contentScale = ContentScale.Fit,
                        modifier = Modifier
                            .fillMaxWidth()
                            .heightIn(max = 160.dp)
                            .clip(RoundedCornerShape(8.dp))
                            .clickable { onThumbnailClick(turn.thumbnailBase64!!) }
                            .padding(bottom = 10.dp)
                    )
                }
            }

            if (turn.text.isNotEmpty()) {
                MarkdownText(
                    markdown = turn.text,
                    color = if (turn.isError) PhantomDanger else PhantomText,
                    modifier = Modifier.fillMaxWidth()
                )
            }

            if (turn.isStreaming) {
                Spacer(modifier = Modifier.height(6.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(12.dp),
                        color = PhantomAccent,
                        strokeWidth = 2.dp
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(
                        text = "Thinking…",
                        fontSize = 12.sp,
                        color = PhantomAccent,
                        fontFamily = FontFamily.Monospace
                    )
                }
            }
        }
    }
}
