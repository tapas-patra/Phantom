package com.phantom.companion.ui.gate

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.MarkEmailUnread
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.phantom.companion.ui.components.PhantomPrimaryButton
import com.phantom.companion.ui.components.PhantomSecondaryButton
import com.phantom.companion.ui.theme.PhantomBackground
import com.phantom.companion.ui.theme.PhantomDanger
import com.phantom.companion.ui.theme.PhantomMuted
import com.phantom.companion.ui.theme.PhantomPrimary
import com.phantom.companion.ui.theme.PhantomSurfaceHigh
import com.phantom.companion.ui.theme.PhantomText
import com.phantom.companion.ui.theme.PhantomWarning

@Composable
fun AccountGateScreen(
    reason: String,
    email: String,
    onRetry: () -> Unit,
    onSignOut: () -> Unit
) {
    val isEmailNotVerified = reason == "email_not_verified"
    val isAccountLocked = reason == "account_locked"

    val title = when {
        isEmailNotVerified -> "Verify your email"
        isAccountLocked -> "Account locked"
        else -> "Connection required"
    }

    val description = when {
        isEmailNotVerified -> "Verify your email on the Phantom website, then try again."
        isAccountLocked -> "This account is temporarily locked. Please contact support or visit the Phantom website."
        else -> "Unable to reach the Phantom service. The server may still be waking up."
    }

    val icon = when {
        isEmailNotVerified -> Icons.Default.MarkEmailUnread
        isAccountLocked -> Icons.Default.Lock
        else -> Icons.Default.Warning
    }

    val iconTint = when {
        isEmailNotVerified -> PhantomPrimary
        isAccountLocked -> PhantomDanger
        else -> PhantomWarning
    }
    val context = LocalContext.current

    Scaffold(
        containerColor = PhantomBackground
    ) { innerPadding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding)
                .padding(horizontal = 24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center
        ) {
            Box(
                modifier = Modifier
                    .size(80.dp)
                    .clip(CircleShape)
                    .background(PhantomSurfaceHigh),
                contentAlignment = Alignment.Center
            ) {
                Icon(
                    imageVector = icon,
                    contentDescription = null,
                    tint = iconTint,
                    modifier = Modifier.size(40.dp)
                )
            }

            Spacer(modifier = Modifier.height(24.dp))

            Text(
                text = title,
                fontSize = 22.sp,
                fontWeight = FontWeight.Bold,
                color = PhantomText,
                fontFamily = FontFamily.SansSerif,
                textAlign = TextAlign.Center
            )

            if (email.isNotEmpty()) {
                Spacer(modifier = Modifier.height(6.dp))
                Text(
                    text = email,
                    fontSize = 13.sp,
                    color = PhantomMuted,
                    fontFamily = FontFamily.Monospace,
                    textAlign = TextAlign.Center
                )
            }

            Spacer(modifier = Modifier.height(16.dp))

            Text(
                text = description,
                fontSize = 14.sp,
                color = PhantomMuted,
                fontFamily = FontFamily.SansSerif,
                textAlign = TextAlign.Center,
                lineHeight = 20.sp,
                modifier = Modifier.padding(horizontal = 16.dp)
            )

            Spacer(modifier = Modifier.height(36.dp))

            if (isEmailNotVerified) {
                PhantomPrimaryButton(
                    text = "Open Phantom website",
                    onClick = {
                        try {
                            val intent = Intent(
                                Intent.ACTION_VIEW,
                                Uri.parse("https://phantom-interview.vercel.app")
                            )
                            context.startActivity(intent)
                        } catch (e: Exception) {
                            // ignore
                        }
                    },
                    testTag = "button_open_web_portal"
                )

                Spacer(modifier = Modifier.height(12.dp))

                PhantomSecondaryButton(
                    text = "Check verification status",
                    onClick = onRetry,
                    modifier = Modifier.fillMaxWidth(),
                    testTag = "button_gate_retry"
                )
            } else {
                PhantomPrimaryButton(
                    text = "Check again",
                    onClick = onRetry,
                    testTag = "button_gate_retry"
                )
            }

            Spacer(modifier = Modifier.height(12.dp))

            PhantomSecondaryButton(
                text = "Sign out",
                onClick = onSignOut,
                modifier = Modifier.fillMaxWidth(),
                testTag = "button_gate_sign_out"
            )
        }
    }
}
