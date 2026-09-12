package com.phantom.companion.ui.pair

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
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
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.filled.CameraAlt
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.phantom.companion.ui.components.ErrorBanner
import com.phantom.companion.ui.components.PhantomPrimaryButton
import com.phantom.companion.ui.components.PhantomSecondaryButton
import com.phantom.companion.ui.components.WarningBanner
import com.phantom.companion.ui.theme.PhantomBackground
import com.phantom.companion.ui.theme.PhantomLine
import com.phantom.companion.ui.theme.PhantomMuted
import com.phantom.companion.ui.theme.PhantomPrimary
import com.phantom.companion.ui.theme.PhantomSurface
import com.phantom.companion.ui.theme.PhantomSurfaceHigh
import com.phantom.companion.ui.theme.PhantomText
import java.util.concurrent.Executors

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PairScreen(
    viewModel: PairViewModel,
    onNavigateToSession: () -> Unit,
    onNavigateToAccount: () -> Unit,
    initialCode: String? = null
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val focusManager = LocalFocusManager.current

    val code by viewModel.code.collectAsState()
    val isLoading by viewModel.isLoading.collectAsState()
    val errorMessage by viewModel.errorMessage.collectAsState()
    val isBackendNotReady by viewModel.isBackendNotReady.collectAsState()

    var hasCameraPermission by remember {
        mutableStateOf(
            ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED
        )
    }

    val cameraPermissionLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.RequestPermission()
    ) { isGranted ->
        hasCameraPermission = isGranted
    }

    LaunchedEffect(initialCode) {
        if (!initialCode.isNullOrEmpty()) {
            viewModel.onCodeChanged(initialCode)
        }
    }

    LaunchedEffect(Unit) {
        viewModel.navigationEvent.collect { event ->
            when (event) {
                is PairNavigationEvent.NavigateToSession -> onNavigateToSession()
                is PairNavigationEvent.NavigateToAccount -> onNavigateToAccount()
            }
        }
    }

    Scaffold(
        containerColor = PhantomBackground,
        topBar = {
            TopAppBar(
                title = {
                    Text(
                        text = "Pair Desktop",
                        fontSize = 18.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = PhantomText
                    )
                },
                actions = {
                    IconButton(
                        onClick = onNavigateToAccount,
                        modifier = Modifier.testTag("button_account_overflow")
                    ) {
                        Icon(
                            imageVector = Icons.Default.AccountCircle,
                            contentDescription = "Account",
                            tint = PhantomMuted
                        )
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = PhantomBackground
                )
            )
        }
    ) { innerPadding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding)
                .padding(horizontal = 24.dp)
                .verticalScroll(rememberScrollState())
                .imePadding(),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Spacer(modifier = Modifier.height(12.dp))

            // Hint
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(12.dp))
                    .background(PhantomSurfaceHigh)
                    .border(1.dp, PhantomLine, RoundedCornerShape(12.dp))
                    .padding(14.dp)
            ) {
                Text(
                    text = "Open Phantom on your computer → Settings → Companion",
                    fontSize = 13.sp,
                    color = PhantomMuted,
                    fontFamily = FontFamily.SansSerif,
                    lineHeight = 18.sp
                )
            }

            Spacer(modifier = Modifier.height(20.dp))

            if (isBackendNotReady) {
                WarningBanner(
                    message = "Desktop companion service is not enabled on this backend yet.",
                    modifier = Modifier.padding(bottom = 16.dp)
                )
            }

            if (!errorMessage.isNullOrEmpty()) {
                ErrorBanner(
                    message = errorMessage!!,
                    modifier = Modifier.padding(bottom = 16.dp)
                )
            }

            // QR Camera scanner box
            Text(
                text = "Scan desktop QR",
                fontSize = 14.sp,
                fontWeight = FontWeight.Medium,
                color = PhantomText,
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(bottom = 10.dp)
            )

            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(220.dp)
                    .clip(RoundedCornerShape(14.dp))
                    .background(PhantomSurface)
                    .border(1.dp, PhantomLine, RoundedCornerShape(14.dp)),
                contentAlignment = Alignment.Center
            ) {
                if (hasCameraPermission) {
                    val cameraExecutor = remember { Executors.newSingleThreadExecutor() }
                    val qrAnalyzer = remember {
                        QrCodeAnalyzer { scannedText ->
                            viewModel.onQrScanned(scannedText)
                        }
                    }

                    AndroidView(
                        factory = { ctx ->
                            val previewView = PreviewView(ctx)
                            val cameraProviderFuture = ProcessCameraProvider.getInstance(ctx)
                            cameraProviderFuture.addListener({
                                val cameraProvider = cameraProviderFuture.get()
                                val preview = Preview.Builder().build().also {
                                    it.surfaceProvider = previewView.surfaceProvider
                                }
                                val imageAnalysis = ImageAnalysis.Builder()
                                    .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                                    .build()
                                    .also {
                                        it.setAnalyzer(cameraExecutor, qrAnalyzer)
                                    }

                                try {
                                    cameraProvider.unbindAll()
                                    cameraProvider.bindToLifecycle(
                                        lifecycleOwner,
                                        CameraSelector.DEFAULT_BACK_CAMERA,
                                        preview,
                                        imageAnalysis
                                    )
                                } catch (e: Exception) {
                                    // Camera bind failed
                                }
                            }, ContextCompat.getMainExecutor(ctx))
                            previewView
                        },
                        modifier = Modifier.fillMaxSize()
                    )

                    // Target scanning crosshair overlay
                    Box(
                        modifier = Modifier
                            .size(150.dp)
                            .border(2.dp, PhantomPrimary.copy(alpha = 0.8f), RoundedCornerShape(12.dp))
                    )
                } else {
                    Column(
                        horizontalAlignment = Alignment.CenterHorizontally,
                        modifier = Modifier.padding(20.dp)
                    ) {
                        Icon(
                            imageVector = Icons.Default.CameraAlt,
                            contentDescription = null,
                            tint = PhantomMuted,
                            modifier = Modifier.size(36.dp)
                        )
                        Spacer(modifier = Modifier.height(10.dp))
                        Text(
                            text = "Camera permission needed to scan QR",
                            fontSize = 13.sp,
                            color = PhantomMuted,
                            textAlign = TextAlign.Center
                        )
                        Spacer(modifier = Modifier.height(14.dp))
                        PhantomSecondaryButton(
                            text = "Enable camera",
                            onClick = { cameraPermissionLauncher.launch(Manifest.permission.CAMERA) },
                            testTag = "button_enable_camera"
                        )
                    }
                }
            }

            Spacer(modifier = Modifier.height(28.dp))

            // 6-Character manual code input
            Text(
                text = "Or enter 6-character code",
                fontSize = 14.sp,
                fontWeight = FontWeight.Medium,
                color = PhantomText,
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(modifier = Modifier.height(12.dp))

            // 6 separate visual boxes for the code
            BasicTextField(
                value = code,
                onValueChange = viewModel::onCodeChanged,
                keyboardOptions = KeyboardOptions(
                    capitalization = KeyboardCapitalization.Characters,
                    imeAction = ImeAction.Done
                ),
                keyboardActions = KeyboardActions(
                    onDone = {
                        focusManager.clearFocus()
                        if (code.length == 6) {
                            viewModel.submitPairing()
                        }
                    }
                ),
                modifier = Modifier
                    .fillMaxWidth()
                    .testTag("input_pairing_code"),
                decorationBox = {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        for (i in 0 until 6) {
                            val char = if (i < code.length) code[i].toString() else ""
                            val isFocused = i == code.length
                            Box(
                                modifier = Modifier
                                    .size(48.dp)
                                    .clip(RoundedCornerShape(10.dp))
                                    .background(PhantomSurface)
                                    .border(
                                        width = if (isFocused) 2.dp else 1.dp,
                                        color = if (isFocused) PhantomPrimary else PhantomLine,
                                        shape = RoundedCornerShape(10.dp)
                                    ),
                                contentAlignment = Alignment.Center
                            ) {
                                Text(
                                    text = char,
                                    fontSize = 20.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = PhantomText,
                                    fontFamily = FontFamily.Monospace
                                )
                            }
                        }
                    }
                }
            )

            Spacer(modifier = Modifier.height(28.dp))

            PhantomPrimaryButton(
                text = if (isLoading) "Pairing…" else "Pair with desktop",
                onClick = {
                    focusManager.clearFocus()
                    viewModel.submitPairing()
                },
                enabled = !isLoading && code.length == 6 && !isBackendNotReady,
                isLoading = isLoading,
                testTag = "button_complete_pairing"
            )

            Spacer(modifier = Modifier.height(36.dp))
        }
    }
}
