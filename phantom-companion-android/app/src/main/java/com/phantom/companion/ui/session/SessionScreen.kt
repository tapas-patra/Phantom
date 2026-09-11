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
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.filled.CameraAlt
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
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.phantom.companion.data.local.PhoneSpeechSession
import com.phantom.companion.data.remote.SocketConnectionState
import com.phantom.companion.domain.model.ChatTurn
import com.phantom.companion.domain.model.DesktopPresenceState
import com.phantom.companion.ui.components.ErrorBanner
import com.phantom.companion.ui.components.MarkdownText
import com.phantom.companion.ui.components.PhantomPrimaryButton
import com.phantom.companion.ui.components.PhantomSecondaryButton
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
    val isVisionSupported by viewModel.isVisionSupported.collectAsState()
    val transcript by viewModel.transcript.collectAsState()
    val currentPendingThumbnail by viewModel.currentPendingThumbnail.collectAsState()
    val sessionError by viewModel.sessionError.collectAsState()
    val activePairing by viewModel.activePairing.collectAsState()
    val companionApiReady by viewModel.companionApiReady.collectAsState()
    val startupSnapshot by viewModel.startupSnapshot.collectAsState()

    val inputText by viewModel.inputText.collectAsState()
    val showNewTopicConfirmDialog by viewModel.showNewTopicConfirmDialog.collectAsState()
    val showVoiceSettings by viewModel.showVoiceSettings.collectAsState()
    val usePhoneMicrophone by viewModel.usePhoneMicrophone.collectAsState()
    val isMicListening by viewModel.isMicListening.collectAsState()

    val listState = rememberLazyListState()
    val relayLive = connectionState is SocketConnectionState.Connected

    val speechSession = remember(context) {
        PhoneSpeechSession(
            context = context,
            onPartial = { viewModel.appendDictatedText(it) },
            onFinal = {
                viewModel.appendDictatedText(it)
                viewModel.setMicListening(false)
            },
            onError = { viewModel.setMicListening(false) }
        )
    }
    DisposableEffect(speechSession) {
        onDispose { speechSession.stop() }
    }
    val micPermissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (granted) {
            viewModel.setMicListening(true)
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

    Scaffold(
        containerColor = PhantomBackground,
        topBar = {
            Surface(
                color = PhantomSurfaceHigh,
                modifier = Modifier
                    .fillMaxWidth()
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
                        val model = currentModel ?: "Phantom AI"

                        Text(
                            text = if (email.isNotEmpty()) "$truncatedEmail · $tier · $model" else model,
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
                            contentDescription = "Voice settings",
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
                    .padding(16.dp)
                    .imePadding()
            ) {
                val busy = desktopPresence == DesktopPresenceState.THINKING ||
                        desktopPresence == DesktopPresenceState.CAPTURING
                val canCapture = companionApiReady != false &&
                        activePairing != null &&
                        relayLive &&
                        isVisionSupported &&
                        !busy

                PhantomPrimaryButton(
                    text = if (desktopPresence == DesktopPresenceState.CAPTURING) "Capturing…" else "Capture & Ask",
                    onClick = viewModel::onCaptureAndAskClicked,
                    enabled = canCapture,
                    isLoading = desktopPresence == DesktopPresenceState.CAPTURING,
                    icon = Icons.Default.CameraAlt,
                    testTag = "button_capture_and_ask"
                )

                Spacer(modifier = Modifier.height(10.dp))

                PhantomSecondaryButton(
                    text = "Capture only",
                    onClick = viewModel::onCaptureOnlyClicked,
                    enabled = canCapture,
                    modifier = Modifier.fillMaxWidth(),
                    testTag = "button_capture_only"
                )

                Spacer(modifier = Modifier.height(12.dp))

                // Follow-up message composer
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically
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
                            .testTag("input_follow_up"),
                        singleLine = true,
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
                        onClick = {
                            if (usePhoneMicrophone) {
                                if (isMicListening) {
                                    speechSession.stop()
                                    viewModel.setMicListening(false)
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
                        enabled = activePairing != null && (usePhoneMicrophone || relayLive),
                        modifier = Modifier
                            .size(48.dp)
                            .clip(RoundedCornerShape(12.dp))
                            .background(if (isMicListening) PhantomDanger else PhantomSurface)
                            .testTag("button_mic")
                    ) {
                        Icon(
                            imageVector = if (isMicListening) Icons.Default.MicOff else Icons.Default.Mic,
                            contentDescription = if (isMicListening) "Stop listening" else "Start voice input",
                            tint = if (isMicListening) Color.White else PhantomText
                        )
                    }

                    Spacer(modifier = Modifier.width(8.dp))

                    val canSend = inputText.isNotBlank() &&
                            desktopPresence != DesktopPresenceState.THINKING &&
                            activePairing != null &&
                            relayLive

                    IconButton(
                        onClick = viewModel::onSendFollowUpClicked,
                        enabled = canSend,
                        modifier = Modifier
                            .size(48.dp)
                            .clip(RoundedCornerShape(12.dp))
                            .background(if (canSend) PhantomPrimary else PhantomSurface)
                            .testTag("button_send_follow_up")
                    ) {
                        Icon(
                            imageVector = Icons.AutoMirrored.Filled.Send,
                            contentDescription = "Send",
                            tint = if (canSend) Color.White else PhantomMuted
                        )
                    }
                }

                // New topic link button
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.Center
                ) {
                    TextButton(
                        onClick = viewModel::showNewTopicDialog,
                        modifier = Modifier.testTag("button_new_topic")
                    ) {
                        Text(
                            text = "New topic",
                            fontSize = 13.sp,
                            color = PhantomMuted
                        )
                    }
                }
            }
        }
    ) { innerPadding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding)
        ) {
            // Notice banners
            if (companionApiReady == false) {
                WarningBanner(
                    message = "Desktop companion service is not enabled on this backend yet.",
                    modifier = Modifier.padding(16.dp)
                )
            }

            if (!sessionError.isNullOrEmpty()) {
                ErrorBanner(
                    message = sessionError!!,
                    modifier = Modifier.padding(16.dp),
                    onDismiss = viewModel::clearError
                )
            }

            if (!isVisionSupported) {
                WarningBanner(
                    message = "This model cannot read screens.",
                    modifier = Modifier.padding(16.dp)
                )
            }

            // Chat Transcript
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
                                text = "Desktop is ready. Tap Capture & Ask.",
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
                        ChatTurnBubble(turn = turn)
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
                    text = "Voice input",
                    color = PhantomText,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.SemiBold
                )
            },
            text = {
                Column {
                    Text(
                        text = "Use this phone’s microphone instead of the desktop microphone.",
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
}

@Composable
fun ChatTurnBubble(turn: ChatTurn) {
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
                    try {
                        val bytes = Base64.decode(turn.thumbnailBase64, Base64.DEFAULT)
                        BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
                    } catch (e: Exception) {
                        null
                    }
                }
                if (bitmap != null) {
                    Image(
                        bitmap = bitmap,
                        contentDescription = "Screen capture thumbnail",
                        modifier = Modifier
                            .fillMaxWidth()
                            .heightIn(max = 160.dp)
                            .clip(RoundedCornerShape(8.dp))
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
