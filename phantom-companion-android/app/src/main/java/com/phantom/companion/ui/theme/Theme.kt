package com.phantom.companion.ui.theme

import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp

private val DarkColorScheme = darkColorScheme(
    primary = PhantomPrimary,
    onPrimary = PhantomText,
    primaryContainer = PhantomPrimaryDark,
    onPrimaryContainer = PhantomText,
    secondary = PhantomAccent,
    onSecondary = PhantomBackground,
    secondaryContainer = PhantomSurfaceHigh,
    onSecondaryContainer = PhantomText,
    tertiary = PhantomSuccess,
    onTertiary = PhantomBackground,
    background = PhantomBackground,
    onBackground = PhantomText,
    surface = PhantomSurface,
    onSurface = PhantomText,
    surfaceVariant = PhantomSurfaceHigh,
    onSurfaceVariant = PhantomMuted,
    outline = PhantomLine,
    error = PhantomDanger,
    onError = PhantomText
)

val PhantomShapes = Shapes(
    small = RoundedCornerShape(10.dp),
    medium = RoundedCornerShape(14.dp),
    large = RoundedCornerShape(22.dp)
)

@Composable
fun PhantomTheme(
    content: @Composable () -> Unit
) {
    MaterialTheme(
        colorScheme = DarkColorScheme,
        typography = Typography,
        shapes = PhantomShapes,
        content = content
    )
}
