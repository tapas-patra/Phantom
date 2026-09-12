package com.phantom.companion.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MenuAnchorType
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
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
import com.phantom.companion.domain.model.DesktopPresenceState
import com.phantom.companion.domain.model.ProviderOption
import com.phantom.companion.ui.theme.PhantomAccent
import com.phantom.companion.ui.theme.PhantomBackground
import com.phantom.companion.ui.theme.PhantomDanger
import com.phantom.companion.ui.theme.PhantomLine
import com.phantom.companion.ui.theme.PhantomMuted
import com.phantom.companion.ui.theme.PhantomPrimary
import com.phantom.companion.ui.theme.PhantomPrimaryDark
import com.phantom.companion.ui.theme.PhantomSuccess
import com.phantom.companion.ui.theme.PhantomSurface
import com.phantom.companion.ui.theme.PhantomSurfaceHigh
import com.phantom.companion.ui.theme.PhantomText
import com.phantom.companion.ui.theme.PhantomWarning

@Composable
fun PhantomPrimaryButton(
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    isLoading: Boolean = false,
    icon: ImageVector? = null,
    testTag: String = "primary_button"
) {
    Button(
        onClick = onClick,
        enabled = enabled && !isLoading,
        modifier = modifier
            .fillMaxWidth()
            .height(52.dp)
            .testTag(testTag),
        shape = RoundedCornerShape(14.dp),
        colors = ButtonDefaults.buttonColors(
            containerColor = PhantomPrimary,
            contentColor = Color.White,
            disabledContainerColor = PhantomSurfaceHigh,
            disabledContentColor = PhantomMuted
        )
    ) {
        if (isLoading) {
            CircularProgressIndicator(
                modifier = Modifier.size(20.dp),
                color = Color.White,
                strokeWidth = 2.dp
            )
        } else {
            Row(verticalAlignment = Alignment.CenterVertically) {
                if (icon != null) {
                    Icon(
                        imageVector = icon,
                        contentDescription = null,
                        modifier = Modifier.size(18.dp)
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                }
                Text(
                    text = text,
                    fontSize = 15.sp,
                    fontWeight = FontWeight.SemiBold,
                    fontFamily = FontFamily.SansSerif
                )
            }
        }
    }
}

@Composable
fun PhantomSecondaryButton(
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    icon: ImageVector? = null,
    testTag: String = "secondary_button"
) {
    OutlinedButton(
        onClick = onClick,
        enabled = enabled,
        modifier = modifier
            .defaultMinSize(minHeight = 48.dp)
            .testTag(testTag),
        shape = RoundedCornerShape(14.dp),
        colors = ButtonDefaults.outlinedButtonColors(
            containerColor = PhantomSurfaceHigh,
            contentColor = PhantomText,
            disabledContainerColor = PhantomBackground,
            disabledContentColor = PhantomMuted
        ),
        border = ButtonDefaults.outlinedButtonBorder(enabled = enabled).copy(
            brush = androidx.compose.ui.graphics.SolidColor(PhantomLine)
        )
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            if (icon != null) {
                Icon(
                    imageVector = icon,
                    contentDescription = null,
                    modifier = Modifier.size(16.dp)
                )
                Spacer(modifier = Modifier.width(6.dp))
            }
            Text(
                text = text,
                fontSize = 14.sp,
                fontWeight = FontWeight.Medium,
                fontFamily = FontFamily.SansSerif
            )
        }
    }
}

@Composable
fun StatusPill(
    presence: DesktopPresenceState,
    modifier: Modifier = Modifier,
    customText: String? = null
) {
    val dotColor = when (presence) {
        DesktopPresenceState.READY -> PhantomSuccess
        DesktopPresenceState.CAPTURING, DesktopPresenceState.THINKING, DesktopPresenceState.IDLE -> PhantomAccent
        DesktopPresenceState.CONNECTING -> PhantomWarning
        DesktopPresenceState.ERROR -> PhantomDanger
        DesktopPresenceState.OFFLINE -> PhantomMuted
    }

    val label = customText ?: presence.label

    Box(
        modifier = modifier
            .clip(RoundedCornerShape(8.dp))
            .background(PhantomSurfaceHigh)
            .border(1.dp, PhantomLine, RoundedCornerShape(8.dp))
            .padding(horizontal = 10.dp, vertical = 5.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(
                modifier = Modifier
                    .size(8.dp)
                    .clip(CircleShape)
                    .background(dotColor)
            )
            Spacer(modifier = Modifier.width(8.dp))
            Text(
                text = label,
                fontSize = 12.sp,
                fontFamily = FontFamily.Monospace,
                color = PhantomText,
                fontWeight = FontWeight.Medium
            )
        }
    }
}

@Composable
fun ErrorBanner(
    message: String,
    modifier: Modifier = Modifier,
    onDismiss: (() -> Unit)? = null
) {
    Box(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .background(Color(0xFF2B141B))
            .border(1.dp, PhantomDanger.copy(alpha = 0.6f), RoundedCornerShape(12.dp))
            .padding(14.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(
                modifier = Modifier
                    .size(8.dp)
                    .clip(CircleShape)
                    .background(PhantomDanger)
            )
            Spacer(modifier = Modifier.width(10.dp))
            Text(
                text = message,
                fontSize = 13.sp,
                color = PhantomText,
                modifier = Modifier.weight(1f)
            )
        }
    }
}

@Composable
fun WarningBanner(
    message: String,
    modifier: Modifier = Modifier
) {
    Box(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .background(Color(0xFF281F12))
            .border(1.dp, PhantomWarning.copy(alpha = 0.6f), RoundedCornerShape(12.dp))
            .padding(14.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(
                modifier = Modifier
                    .size(8.dp)
                    .clip(CircleShape)
                    .background(PhantomWarning)
            )
            Spacer(modifier = Modifier.width(10.dp))
            Text(
                text = message,
                fontSize = 13.sp,
                color = PhantomText,
                modifier = Modifier.weight(1f)
            )
        }
    }
}

@Composable
fun MarkdownText(
    markdown: String,
    color: Color,
    modifier: Modifier = Modifier
) {
    val html = remember(markdown) { markdownToHtml(markdown) }
    val androidColor = android.graphics.Color.argb(
        (color.alpha * 255).toInt(),
        (color.red * 255).toInt(),
        (color.green * 255).toInt(),
        (color.blue * 255).toInt()
    )
    androidx.compose.ui.viewinterop.AndroidView(
        modifier = modifier.fillMaxWidth(),
        factory = { ctx ->
            android.widget.TextView(ctx).apply {
                textSize = 14f
                setLineSpacing(0f, 1.2f)
                setTextIsSelectable(true)
                setHorizontallyScrolling(false)
                layoutParams = android.view.ViewGroup.LayoutParams(
                    android.view.ViewGroup.LayoutParams.MATCH_PARENT,
                    android.view.ViewGroup.LayoutParams.WRAP_CONTENT
                )
            }
        },
        update = { tv ->
            tv.setTextColor(androidColor)
            tv.text = android.text.Html.fromHtml(html, android.text.Html.FROM_HTML_MODE_LEGACY)
        }
    )
}

internal fun markdownToHtml(source: String): String {
    val escaped = source
        .replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
    val placeholders = mutableListOf<String>()
    val withCode = escaped.replace(Regex("```[a-zA-Z]*\\n([\\s\\S]*?)```")) { match ->
        placeholders.add("<pre>${match.groupValues[1].trim().replace("\n", "<br>")}</pre>")
        "\u0000CODE${placeholders.lastIndex}\u0000"
    }
    val withInline = withCode
        .replace(Regex("`([^`]+)`"), "<code>$1</code>")
        .replace(Regex("\\*\\*(.+?)\\*\\*"), "<b>$1</b>")
        .replace(Regex("(?m)^### (.+)$"), "<h4>$1</h4>")
        .replace(Regex("(?m)^## (.+)$"), "<h3>$1</h3>")
        .replace(Regex("(?m)^# (.+)$"), "<h2>$1</h2>")
        .replace(Regex("(?m)^[-*] (.+)$"), "• $1")
        .replace("\n", "<br>")
    return withInline.replace(Regex("\u0000CODE(\\d+)\u0000")) { match ->
        placeholders[match.groupValues[1].toInt()]
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ProviderModelPickers(
    providers: List<ProviderOption>,
    selectedProviderId: String,
    selectedModelId: String,
    onSelect: (providerId: String, modelId: String) -> Unit,
    enabled: Boolean = true
) {
    if (providers.isEmpty()) {
        Text(
            text = "Waiting for the desktop catalog…",
            fontSize = 13.sp,
            color = PhantomMuted
        )
        return
    }

    val provider = providers.firstOrNull { it.id.equals(selectedProviderId, ignoreCase = true) }
        ?: providers.first()
    val models = provider.models
    val selectedModel = models.firstOrNull { it.id.equals(selectedModelId, ignoreCase = true) }
        ?: models.firstOrNull()

    Column(modifier = Modifier.fillMaxWidth()) {
        PhantomDropdown(
            label = "Provider",
            selectedId = provider.id,
            selectedLabel = provider.name.ifBlank { provider.id },
            options = providers.map { it.id to it.name.ifBlank { it.id } },
            enabled = enabled,
            testTag = "dropdown_provider",
            onSelect = { providerId ->
                val next = providers.firstOrNull { it.id == providerId } ?: return@PhantomDropdown
                val keep = next.models.firstOrNull { it.id.equals(selectedModelId, ignoreCase = true) }
                val modelId = keep?.id ?: next.models.firstOrNull()?.id ?: ""
                if (modelId.isNotEmpty()) onSelect(next.id, modelId)
            }
        )
        Spacer(modifier = Modifier.height(12.dp))
        PhantomDropdown(
            label = "Model",
            selectedId = selectedModel?.id.orEmpty(),
            selectedLabel = selectedModel?.name?.ifBlank { selectedModel.id } ?: selectedModelId.ifBlank { "Select model" },
            options = models.map { it.id to it.name.ifBlank { it.id } },
            enabled = enabled && models.isNotEmpty(),
            testTag = "dropdown_model",
            onSelect = { modelId ->
                onSelect(provider.id, modelId)
            }
        )
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PhantomDropdown(
    label: String,
    selectedId: String,
    selectedLabel: String,
    options: List<Pair<String, String>>,
    onSelect: (String) -> Unit,
    enabled: Boolean = true,
    testTag: String = "dropdown"
) {
    var expanded by remember { mutableStateOf(false) }
    ExposedDropdownMenuBox(
        expanded = expanded,
        onExpandedChange = { if (enabled) expanded = !expanded },
        modifier = Modifier.fillMaxWidth()
    ) {
        OutlinedTextField(
            value = selectedLabel.ifBlank { selectedId },
            onValueChange = {},
            readOnly = true,
            enabled = enabled,
            label = { Text(label) },
            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = expanded) },
            modifier = Modifier
                .fillMaxWidth()
                .menuAnchor(type = MenuAnchorType.PrimaryNotEditable, enabled = enabled)
                .testTag(testTag),
            colors = OutlinedTextFieldDefaults.colors(
                focusedTextColor = PhantomText,
                unfocusedTextColor = PhantomText,
                focusedContainerColor = PhantomSurface,
                unfocusedContainerColor = PhantomSurface,
                focusedBorderColor = PhantomPrimary,
                unfocusedBorderColor = PhantomLine,
                focusedLabelColor = PhantomMuted,
                unfocusedLabelColor = PhantomMuted
            )
        )
        ExposedDropdownMenu(
            expanded = expanded,
            onDismissRequest = { expanded = false }
        ) {
            options.forEach { (id, name) ->
                DropdownMenuItem(
                    text = { Text(name.ifBlank { id }) },
                    onClick = {
                        expanded = false
                        if (id != selectedId) onSelect(id)
                    }
                )
            }
        }
    }
}
