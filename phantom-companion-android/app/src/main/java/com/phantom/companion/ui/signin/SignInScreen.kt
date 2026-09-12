package com.phantom.companion.ui.signin

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
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
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.OpenInNew
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusDirection
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.phantom.companion.ui.components.ErrorBanner
import com.phantom.companion.ui.components.PhantomPrimaryButton
import com.phantom.companion.ui.components.WarningBanner
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
fun SignInScreen(
    viewModel: SignInViewModel,
    onNavigateToSession: () -> Unit,
    onNavigateToPair: () -> Unit,
    onNavigateToGate: (reason: String, email: String) -> Unit
) {
    val email by viewModel.email.collectAsState()
    val password by viewModel.password.collectAsState()
    val isLoading by viewModel.isLoading.collectAsState()
    val emailError by viewModel.emailError.collectAsState()
    val passwordError by viewModel.passwordError.collectAsState()
    val generalError by viewModel.generalError.collectAsState()
    val isColdStarting by viewModel.isColdStarting.collectAsState()

    val forgotPasswordDialogVisible by viewModel.forgotPasswordDialogVisible.collectAsState()
    val forgotPasswordEmail by viewModel.forgotPasswordEmail.collectAsState()
    val forgotPasswordMessage by viewModel.forgotPasswordMessage.collectAsState()
    val forgotPasswordLoading by viewModel.forgotPasswordLoading.collectAsState()

    var passwordVisible by remember { mutableStateOf(false) }
    val focusManager = LocalFocusManager.current
    val context = LocalContext.current

    LaunchedEffect(Unit) {
        viewModel.navigationEvent.collect { event ->
            when (event) {
                is SignInNavigationEvent.NavigateToSession -> onNavigateToSession()
                is SignInNavigationEvent.NavigateToPair -> onNavigateToPair()
                is SignInNavigationEvent.NavigateToGate -> onNavigateToGate(event.reason, event.email)
            }
        }
    }

    Scaffold(
        containerColor = PhantomBackground
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
            Spacer(modifier = Modifier.height(48.dp))

            // Wordmark header
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text(
                    text = "Phantom",
                    fontSize = 36.sp,
                    fontWeight = FontWeight.Bold,
                    color = PhantomText,
                    fontFamily = FontFamily.SansSerif,
                    letterSpacing = (-0.5).sp
                )
                Text(
                    text = "COMPANION",
                    fontSize = 12.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = PhantomPrimary,
                    fontFamily = FontFamily.Monospace,
                    letterSpacing = 3.sp
                )
            }

            Spacer(modifier = Modifier.height(12.dp))
            Text(
                text = "Sign in to connect with your desktop client",
                fontSize = 14.sp,
                color = PhantomMuted,
                fontFamily = FontFamily.SansSerif
            )

            Spacer(modifier = Modifier.height(32.dp))

            if (isColdStarting) {
                WarningBanner(
                    message = "Waking Phantom… The server is starting up. This may take up to 60 seconds on first request.",
                    modifier = Modifier.padding(bottom = 16.dp)
                )
            }

            if (!generalError.isNullOrEmpty()) {
                ErrorBanner(
                    message = generalError!!,
                    modifier = Modifier.padding(bottom = 16.dp)
                )
            }

            // Email Input
            Column(modifier = Modifier.fillMaxWidth()) {
                Text(
                    text = "Email",
                    fontSize = 13.sp,
                    fontWeight = FontWeight.Medium,
                    color = PhantomMuted,
                    modifier = Modifier.padding(bottom = 6.dp)
                )
                OutlinedTextField(
                    value = email,
                    onValueChange = viewModel::onEmailChanged,
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("input_email"),
                    placeholder = { Text("user@example.com", color = PhantomMuted.copy(alpha = 0.5f)) },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(
                        keyboardType = KeyboardType.Email,
                        imeAction = ImeAction.Next
                    ),
                    keyboardActions = KeyboardActions(
                        onNext = { focusManager.moveFocus(FocusDirection.Down) }
                    ),
                    shape = RoundedCornerShape(12.dp),
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedTextColor = PhantomText,
                        unfocusedTextColor = PhantomText,
                        focusedContainerColor = PhantomSurface,
                        unfocusedContainerColor = PhantomSurface,
                        focusedBorderColor = PhantomPrimary,
                        unfocusedBorderColor = PhantomLine,
                        errorBorderColor = PhantomDanger
                    ),
                    isError = emailError != null
                )
                if (emailError != null) {
                    Text(
                        text = emailError!!,
                        fontSize = 12.sp,
                        color = PhantomDanger,
                        modifier = Modifier.padding(top = 4.dp, start = 4.dp)
                    )
                }
            }

            Spacer(modifier = Modifier.height(18.dp))

            // Password Input
            Column(modifier = Modifier.fillMaxWidth()) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "Password",
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Medium,
                        color = PhantomMuted,
                        modifier = Modifier.weight(1f)
                    )
                    TextButton(
                        onClick = viewModel::showForgotPasswordDialog,
                        modifier = Modifier.testTag("button_forgot_password")
                    ) {
                        Text(
                            text = "Forgot password?",
                            fontSize = 12.sp,
                            color = PhantomPrimary
                        )
                    }
                }
                OutlinedTextField(
                    value = password,
                    onValueChange = viewModel::onPasswordChanged,
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("input_password"),
                    placeholder = { Text("••••••••", color = PhantomMuted.copy(alpha = 0.5f)) },
                    singleLine = true,
                    visualTransformation = if (passwordVisible) VisualTransformation.None else PasswordVisualTransformation(),
                    keyboardOptions = KeyboardOptions(
                        keyboardType = KeyboardType.Password,
                        imeAction = ImeAction.Done
                    ),
                    keyboardActions = KeyboardActions(
                        onDone = {
                            focusManager.clearFocus()
                            viewModel.onSignInClicked()
                        }
                    ),
                    trailingIcon = {
                        IconButton(
                            onClick = { passwordVisible = !passwordVisible },
                            modifier = Modifier.testTag("toggle_password_visibility")
                        ) {
                            Icon(
                                imageVector = if (passwordVisible) Icons.Default.VisibilityOff else Icons.Default.Visibility,
                                contentDescription = if (passwordVisible) "Hide password" else "Show password",
                                tint = PhantomMuted
                            )
                        }
                    },
                    shape = RoundedCornerShape(12.dp),
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedTextColor = PhantomText,
                        unfocusedTextColor = PhantomText,
                        focusedContainerColor = PhantomSurface,
                        unfocusedContainerColor = PhantomSurface,
                        focusedBorderColor = PhantomPrimary,
                        unfocusedBorderColor = PhantomLine,
                        errorBorderColor = PhantomDanger
                    ),
                    isError = passwordError != null
                )
                if (passwordError != null) {
                    Text(
                        text = passwordError!!,
                        fontSize = 12.sp,
                        color = PhantomDanger,
                        modifier = Modifier.padding(top = 4.dp, start = 4.dp)
                    )
                }
            }

            Spacer(modifier = Modifier.height(28.dp))

            PhantomPrimaryButton(
                text = if (isLoading) "Signing in…" else "Sign in",
                onClick = {
                    focusManager.clearFocus()
                    viewModel.onSignInClicked()
                },
                enabled = !isLoading,
                isLoading = isLoading,
                testTag = "button_sign_in"
            )

            Spacer(modifier = Modifier.weight(1f))
            Spacer(modifier = Modifier.height(24.dp))

            // Footer: Create account link
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.Center,
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(bottom = 24.dp)
            ) {
                Text(
                    text = "Don't have an account?",
                    fontSize = 13.sp,
                    color = PhantomMuted,
                    fontFamily = FontFamily.SansSerif
                )
                Spacer(modifier = Modifier.width(6.dp))
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    modifier = Modifier
                        .clip(RoundedCornerShape(4.dp))
                        .clickable {
                            try {
                                val intent = Intent(
                                    Intent.ACTION_VIEW,
                                    Uri.parse("https://phantom-interview.vercel.app/register")
                                )
                                context.startActivity(intent)
                            } catch (e: Exception) {
                                // fallback if no browser activity
                            }
                        }
                        .testTag("button_create_account")
                        .padding(horizontal = 4.dp, vertical = 2.dp)
                ) {
                    Text(
                        text = "Create account",
                        fontSize = 13.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = PhantomPrimary,
                        fontFamily = FontFamily.SansSerif
                    )
                    Spacer(modifier = Modifier.width(4.dp))
                    Icon(
                        imageVector = Icons.AutoMirrored.Filled.OpenInNew,
                        contentDescription = "Open registration page",
                        tint = PhantomPrimary,
                        modifier = Modifier.size(13.dp)
                    )
                }
            }
        }
    }

    // Forgot password dialog
    if (forgotPasswordDialogVisible) {
        AlertDialog(
            onDismissRequest = viewModel::dismissForgotPasswordDialog,
            containerColor = PhantomSurface,
            title = {
                Text(
                    text = "Reset password",
                    color = PhantomText,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.SemiBold
                )
            },
            text = {
                Column {
                    Text(
                        text = "Enter your account email to receive a password reset link. Password reset is completed on the Phantom website.",
                        color = PhantomMuted,
                        fontSize = 13.sp,
                        lineHeight = 18.sp
                    )
                    Spacer(modifier = Modifier.height(16.dp))
                    OutlinedTextField(
                        value = forgotPasswordEmail,
                        onValueChange = viewModel::onForgotPasswordEmailChanged,
                        modifier = Modifier
                            .fillMaxWidth()
                            .testTag("input_forgot_email"),
                        placeholder = { Text("user@example.com", color = PhantomMuted.copy(alpha = 0.5f)) },
                        singleLine = true,
                        shape = RoundedCornerShape(10.dp),
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedTextColor = PhantomText,
                            unfocusedTextColor = PhantomText,
                            focusedContainerColor = PhantomSurfaceHigh,
                            unfocusedContainerColor = PhantomSurfaceHigh,
                            focusedBorderColor = PhantomPrimary,
                            unfocusedBorderColor = PhantomLine
                        )
                    )

                    if (!forgotPasswordMessage.isNullOrEmpty()) {
                        Spacer(modifier = Modifier.height(12.dp))
                        Text(
                            text = forgotPasswordMessage!!,
                            color = PhantomPrimary,
                            fontSize = 13.sp
                        )
                    }
                }
            },
            confirmButton = {
                PhantomPrimaryButton(
                    text = if (forgotPasswordLoading) "Sending…" else "Send link",
                    onClick = viewModel::submitForgotPassword,
                    enabled = !forgotPasswordLoading && forgotPasswordEmail.isNotBlank(),
                    isLoading = forgotPasswordLoading,
                    modifier = Modifier.width(130.dp),
                    testTag = "button_submit_forgot_password"
                )
            },
            dismissButton = {
                TextButton(
                    onClick = viewModel::dismissForgotPasswordDialog,
                    modifier = Modifier.testTag("button_cancel_forgot_password")
                ) {
                    Text("Close", color = PhantomMuted)
                }
            }
        )
    }
}
