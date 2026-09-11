package com.phantom.companion.ui.account

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
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Computer
import androidx.compose.material.icons.filled.CreditCard
import androidx.compose.material.icons.filled.Memory
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.Person
import androidx.compose.material.icons.filled.PowerSettingsNew
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.phantom.companion.BuildConfig
import com.phantom.companion.ui.components.PhantomPrimaryButton
import com.phantom.companion.ui.components.PhantomSecondaryButton
import com.phantom.companion.ui.components.ProviderModelPickers
import com.phantom.companion.ui.theme.PhantomBackground
import com.phantom.companion.ui.theme.PhantomDanger
import com.phantom.companion.ui.theme.PhantomLine
import com.phantom.companion.ui.theme.PhantomMuted
import com.phantom.companion.ui.theme.PhantomPrimary
import com.phantom.companion.ui.theme.PhantomSuccess
import com.phantom.companion.ui.theme.PhantomSurface
import com.phantom.companion.ui.theme.PhantomSurfaceHigh
import com.phantom.companion.ui.theme.PhantomText
import com.phantom.companion.ui.theme.PhantomWarning

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AccountScreen(
    viewModel: AccountViewModel,
    onNavigateBack: () -> Unit,
    onNavigateToSignIn: () -> Unit,
    onNavigateToPair: () -> Unit
) {
    val startupSnapshot by viewModel.startupSnapshot.collectAsState()
    val activePairing by viewModel.activePairing.collectAsState()
    val companionApiReady by viewModel.companionApiReady.collectAsState()
    val usePhoneMicrophone by viewModel.usePhoneMicrophone.collectAsState()
    val currentProvider by viewModel.currentProvider.collectAsState()
    val currentModel by viewModel.currentModel.collectAsState()
    val providers by viewModel.providers.collectAsState()
    val isUnpairing by viewModel.isUnpairing.collectAsState()
    val isSigningOut by viewModel.isSigningOut.collectAsState()
    val showUnpairConfirmDialog by viewModel.showUnpairConfirmDialog.collectAsState()

    LaunchedEffect(Unit) {
        viewModel.navigationEvent.collect { event ->
            when (event) {
                is AccountNavigationEvent.NavigateToSignIn -> onNavigateToSignIn()
                is AccountNavigationEvent.NavigateToPair -> onNavigateToPair()
            }
        }
    }

    Scaffold(
        containerColor = PhantomBackground,
        topBar = {
            TopAppBar(
                title = {
                    Text(
                        text = "Account",
                        fontSize = 18.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = PhantomText
                    )
                },
                navigationIcon = {
                    IconButton(
                        onClick = onNavigateBack,
                        modifier = Modifier.testTag("button_account_back")
                    ) {
                        Icon(
                            imageVector = Icons.AutoMirrored.Filled.ArrowBack,
                            contentDescription = "Back",
                            tint = PhantomText
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
                .padding(horizontal = 20.dp)
                .verticalScroll(rememberScrollState())
        ) {
            Spacer(modifier = Modifier.height(8.dp))

            // User Profile Section
            AccountCard(title = "Profile", icon = Icons.Default.Person) {
                InfoRow(label = "Email", value = startupSnapshot?.email.orEmpty().ifEmpty { "Signed in" })
                InfoRow(
                    label = "Access Tier",
                    value = startupSnapshot?.accessTier?.replaceFirstChar { it.uppercase() } ?: "Free"
                )
                InfoRow(
                    label = "Status",
                    value = if (startupSnapshot?.emailVerified == true) "Verified" else "Verification required",
                    valueColor = if (startupSnapshot?.emailVerified == true) PhantomSuccess else PhantomWarning
                )
            }

            Spacer(modifier = Modifier.height(16.dp))

            // Wallet Section
            AccountCard(title = "Wallet", icon = Icons.Default.CreditCard) {
                val wallet = startupSnapshot?.wallet
                InfoRow(
                    label = "Pro Credits",
                    value = "%.2f".format(wallet?.proAvailableCredits ?: 0.0)
                )
                InfoRow(
                    label = "Premium Credits",
                    value = "%.2f".format(wallet?.premiumAvailableCredits ?: 0.0)
                )
                if ((wallet?.premiumNegativeCredits ?: 0.0) > 0.0) {
                    InfoRow(
                        label = "Premium Debt",
                        value = "-%.2f".format(wallet?.premiumNegativeCredits),
                        valueColor = PhantomDanger
                    )
                }
            }

            Spacer(modifier = Modifier.height(16.dp))

            // Paired Desktop Section
            AccountCard(title = "Paired Desktop", icon = Icons.Default.Computer) {
                if (activePairing != null) {
                    InfoRow(label = "Device", value = activePairing!!.desktopDeviceLabel.ifEmpty { "Windows Desktop" })
                    InfoRow(label = "Platform", value = activePairing!!.desktopPlatform.ifEmpty { "Windows" })
                    InfoRow(
                        label = "Desktop Status",
                        value = if (activePairing!!.desktopOnline) "Online" else "Offline",
                        valueColor = if (activePairing!!.desktopOnline) PhantomSuccess else PhantomMuted
                    )
                    Spacer(modifier = Modifier.height(12.dp))
                    PhantomSecondaryButton(
                        text = if (isUnpairing) "Unpairing…" else "Unpair desktop",
                        onClick = viewModel::showUnpairDialog,
                        enabled = !isUnpairing,
                        modifier = Modifier.fillMaxWidth(),
                        testTag = "button_unpair_desktop"
                    )
                } else {
                    Text(
                        text = "No desktop paired currently.",
                        fontSize = 13.sp,
                        color = PhantomMuted
                    )
                    Spacer(modifier = Modifier.height(12.dp))
                    PhantomPrimaryButton(
                        text = "Pair desktop",
                        onClick = viewModel::navigateToPair,
                        testTag = "button_account_pair"
                    )
                }
            }

            Spacer(modifier = Modifier.height(16.dp))

            AccountCard(title = "Model", icon = Icons.Default.Memory) {
                Text(
                    text = "Changes apply on this phone and the paired desktop.",
                    fontSize = 13.sp,
                    color = PhantomMuted
                )
                Spacer(modifier = Modifier.height(12.dp))
                ProviderModelPickers(
                    providers = providers,
                    selectedProviderId = currentProvider.orEmpty(),
                    selectedModelId = currentModel.orEmpty(),
                    onSelect = viewModel::selectRuntime,
                    enabled = activePairing != null
                )
            }

            Spacer(modifier = Modifier.height(16.dp))

            AccountCard(title = "Voice input", icon = Icons.Default.Mic) {
                Text(
                    text = "When enabled, the mic button on the session screen uses this phone instead of the desktop microphone.",
                    fontSize = 13.sp,
                    color = PhantomMuted
                )
                Spacer(modifier = Modifier.height(12.dp))
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Text(
                        text = "Use phone microphone",
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Medium,
                        color = PhantomText
                    )
                    Switch(
                        checked = usePhoneMicrophone,
                        onCheckedChange = viewModel::setUsePhoneMicrophone,
                        colors = SwitchDefaults.colors(checkedTrackColor = PhantomPrimary)
                    )
                }
            }

            Spacer(modifier = Modifier.height(16.dp))

            // Backend Service Status Card
            AccountCard(title = "Service Status", icon = Icons.Default.Warning) {
                val isReady = companionApiReady == true
                InfoRow(
                    label = "Companion API",
                    value = if (isReady) "Ready" else "Not deployed on backend",
                    valueColor = if (isReady) PhantomSuccess else PhantomWarning
                )
            }

            Spacer(modifier = Modifier.height(24.dp))

            // Sign Out Button
            PhantomSecondaryButton(
                text = if (isSigningOut) "Signing out…" else "Sign out",
                onClick = viewModel::signOut,
                enabled = !isSigningOut,
                icon = Icons.Default.PowerSettingsNew,
                modifier = Modifier.fillMaxWidth(),
                testTag = "button_account_sign_out"
            )

            Spacer(modifier = Modifier.height(28.dp))

            // Footer
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(bottom = 24.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Text(
                    text = "Phantom Companion v${BuildConfig.VERSION_NAME}",
                    fontSize = 12.sp,
                    color = PhantomMuted,
                    fontFamily = FontFamily.Monospace
                )
                Spacer(modifier = Modifier.height(4.dp))
                val baseClean = BuildConfig.PHANTOM_API_BASE_URL.replace("https://", "").trimEnd('/')
                Text(
                    text = baseClean,
                    fontSize = 11.sp,
                    color = PhantomMuted.copy(alpha = 0.6f),
                    fontFamily = FontFamily.Monospace
                )
            }
        }
    }

    // Unpair confirmation dialog
    if (showUnpairConfirmDialog) {
        AlertDialog(
            onDismissRequest = viewModel::dismissUnpairDialog,
            containerColor = PhantomSurface,
            title = {
                Text(
                    text = "Unpair desktop?",
                    color = PhantomText,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.SemiBold
                )
            },
            text = {
                Text(
                    text = "This will disconnect this phone from your desktop client. You will need to scan a new QR code to reconnect.",
                    color = PhantomMuted,
                    fontSize = 14.sp
                )
            },
            confirmButton = {
                PhantomPrimaryButton(
                    text = "Unpair",
                    onClick = viewModel::confirmUnpair,
                    modifier = Modifier.width(120.dp),
                    testTag = "button_confirm_unpair"
                )
            },
            dismissButton = {
                TextButton(
                    onClick = viewModel::dismissUnpairDialog,
                    modifier = Modifier.testTag("button_cancel_unpair")
                ) {
                    Text("Cancel", color = PhantomMuted)
                }
            }
        )
    }
}

@Composable
private fun AccountCard(
    title: String,
    icon: ImageVector,
    content: @Composable () -> Unit
) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(14.dp))
            .background(PhantomSurface)
            .border(1.dp, PhantomLine, RoundedCornerShape(14.dp))
            .padding(16.dp)
    ) {
        Column {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(
                    imageVector = icon,
                    contentDescription = null,
                    tint = PhantomPrimary,
                    modifier = Modifier.size(18.dp)
                )
                Spacer(modifier = Modifier.width(8.dp))
                Text(
                    text = title,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = PhantomText,
                    fontFamily = FontFamily.SansSerif
                )
            }
            Spacer(modifier = Modifier.height(14.dp))
            content()
        }
    }
}

@Composable
private fun InfoRow(
    label: String,
    value: String,
    valueColor: Color = PhantomText
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 4.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(
            text = label,
            fontSize = 13.sp,
            color = PhantomMuted,
            fontFamily = FontFamily.SansSerif
        )
        Text(
            text = value,
            fontSize = 13.sp,
            fontWeight = FontWeight.Medium,
            color = valueColor,
            fontFamily = FontFamily.Monospace
        )
    }
}
