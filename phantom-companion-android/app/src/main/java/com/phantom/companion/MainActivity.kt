package com.phantom.companion

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import androidx.navigation.navDeepLink
import com.phantom.companion.data.repo.AuthResult
import com.phantom.companion.ui.account.AccountScreen
import com.phantom.companion.ui.account.AccountViewModel
import com.phantom.companion.ui.gate.AccountGateScreen
import com.phantom.companion.ui.nav.NavRoutes
import com.phantom.companion.ui.pair.PairScreen
import com.phantom.companion.ui.pair.PairViewModel
import com.phantom.companion.ui.session.SessionScreen
import com.phantom.companion.ui.session.SessionViewModel
import com.phantom.companion.ui.signin.SignInScreen
import com.phantom.companion.ui.signin.SignInViewModel
import com.phantom.companion.ui.theme.PhantomBackground
import com.phantom.companion.ui.theme.PhantomPrimary
import com.phantom.companion.ui.theme.PhantomTheme

class MainActivity : ComponentActivity() {

    private val appContainer by lazy {
        (application as PhantomApp).container
    }

    private var deepLinkPairingCode: String? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        enableEdgeToEdge()

        extractDeepLinkCode(intent)

        setContent {
            PhantomTheme {
                MainAppNavHost()
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        extractDeepLinkCode(intent)
    }

    private fun extractDeepLinkCode(intent: Intent?) {
        val data: Uri? = intent?.data
        if (data != null && data.scheme == "phantom-companion" && data.host == "pair") {
            val codeParam = data.getQueryParameter("code")
            if (!codeParam.isNullOrEmpty()) {
                deepLinkPairingCode = codeParam.uppercase()
            }
        }
    }

    @Composable
    private fun MainAppNavHost() {
        val navController = rememberNavController()

        val cachedSession = remember { appContainer.sessionStore.getSession() }
        val cachedPairing = remember { appContainer.sessionStore.activePairing.value }
        val startDestination = remember {
            if (cachedSession != null && cachedSession.accessToken.isNotEmpty()) {
                if (cachedPairing != null) NavRoutes.SESSION else NavRoutes.PAIR
            } else {
                NavRoutes.SIGN_IN
            }
        }

        LaunchedEffect(Unit) {
            if (cachedSession != null && cachedSession.accessToken.isNotEmpty()) {
                val startupResult = appContainer.authRepository.checkSessionOnStart()
                when (startupResult) {
                    is AuthResult.EmailNotVerified -> {
                        navController.navigate("${NavRoutes.ACCOUNT_GATE}/email_not_verified/${startupResult.email}") {
                            popUpTo(0) { inclusive = true }
                        }
                    }
                    is AuthResult.AccountLocked -> {
                        navController.navigate("${NavRoutes.ACCOUNT_GATE}/account_locked/${cachedSession.email}") {
                            popUpTo(0) { inclusive = true }
                        }
                    }
                    null -> {
                        // Session expired or revoked -> route to Sign In
                        navController.navigate(NavRoutes.SIGN_IN) {
                            popUpTo(0) { inclusive = true }
                        }
                    }
                    else -> {
                        // Backend online / offline / degraded: current route handles it gracefully
                    }
                }
            }
        }

        NavHost(
            navController = navController,
            startDestination = startDestination
        ) {
            composable(NavRoutes.SIGN_IN) {
                val signInViewModel: SignInViewModel = remember {
                    SignInViewModel(appContainer.authRepository)
                }
                SignInScreen(
                    viewModel = signInViewModel,
                    onNavigateToSession = {
                        val hasPairing = appContainer.sessionStore.activePairing.value != null
                        val destination = if (hasPairing) NavRoutes.SESSION else NavRoutes.PAIR
                        navController.navigate(destination) {
                            popUpTo(NavRoutes.SIGN_IN) { inclusive = true }
                        }
                    },
                    onNavigateToPair = {
                        navController.navigate(NavRoutes.PAIR) {
                            popUpTo(NavRoutes.SIGN_IN) { inclusive = true }
                        }
                    },
                    onNavigateToGate = { reason, email ->
                        navController.navigate("${NavRoutes.ACCOUNT_GATE}/$reason/$email") {
                            popUpTo(NavRoutes.SIGN_IN) { inclusive = true }
                        }
                    }
                )
            }

            composable(
                route = "${NavRoutes.ACCOUNT_GATE}/{reason}/{email}",
                arguments = listOf(
                    navArgument("reason") { type = NavType.StringType },
                    navArgument("email") { type = NavType.StringType }
                )
            ) { backStackEntry ->
                val reason = backStackEntry.arguments?.getString("reason") ?: "connection"
                val email = backStackEntry.arguments?.getString("email") ?: ""
                AccountGateScreen(
                    reason = reason,
                    email = email,
                    onRetry = {
                        navController.navigate(NavRoutes.SIGN_IN) {
                            popUpTo(0) { inclusive = true }
                        }
                    },
                    onSignOut = {
                        appContainer.sessionStore.clearSession()
                        navController.navigate(NavRoutes.SIGN_IN) {
                            popUpTo(0) { inclusive = true }
                        }
                    }
                )
            }

            composable(
                route = NavRoutes.PAIR,
                deepLinks = listOf(
                    navDeepLink { uriPattern = "phantom-companion://pair?code={code}&relay={relay}" },
                    navDeepLink { uriPattern = "phantom-companion://pair?code={code}" },
                    navDeepLink { uriPattern = "phantom-companion://pair" }
                )
            ) { backStackEntry ->
                val codeArg = backStackEntry.arguments?.getString("code") ?: deepLinkPairingCode
                val pairViewModel: PairViewModel = remember {
                    PairViewModel(appContainer.pairingRepository)
                }
                PairScreen(
                    viewModel = pairViewModel,
                    initialCode = codeArg,
                    onNavigateToSession = {
                        navController.navigate(NavRoutes.SESSION) {
                            popUpTo(NavRoutes.PAIR) { inclusive = true }
                        }
                    },
                    onNavigateToAccount = {
                        navController.navigate(NavRoutes.ACCOUNT)
                    }
                )
            }

            composable(NavRoutes.SESSION) {
                val sessionViewModel: SessionViewModel = remember {
                    SessionViewModel(appContainer.sessionRepository, appContainer.sessionStore)
                }
                SessionScreen(
                    viewModel = sessionViewModel,
                    onNavigateToAccount = {
                        navController.navigate(NavRoutes.ACCOUNT)
                    },
                    onNavigateToPair = {
                        navController.navigate(NavRoutes.PAIR)
                    }
                )
            }

            composable(NavRoutes.ACCOUNT) {
                val accountViewModel: AccountViewModel = remember {
                    AccountViewModel(
                        appContainer.authRepository,
                        appContainer.pairingRepository,
                        appContainer.sessionStore
                    )
                }
                AccountScreen(
                    viewModel = accountViewModel,
                    onNavigateBack = {
                        navController.popBackStack()
                    },
                    onNavigateToSignIn = {
                        navController.navigate(NavRoutes.SIGN_IN) {
                            popUpTo(0) { inclusive = true }
                        }
                    },
                    onNavigateToPair = {
                        navController.navigate(NavRoutes.PAIR) {
                            popUpTo(NavRoutes.ACCOUNT) { inclusive = true }
                        }
                    }
                )
            }
        }
    }
}
