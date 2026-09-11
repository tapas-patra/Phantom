package com.phantom.companion

import com.phantom.companion.ui.components.markdownToHtml
import org.junit.Assert.assertTrue
import org.junit.Test

class MarkdownTest {
    @Test
    fun boldAndCodeFenceBecomeHtml() {
        val html = markdownToHtml(
            """
            **JSON** is simple.

            ```json
            {
              "ok": true
            }
            ```
            """.trimIndent()
        )
        assertTrue(html.contains("<b>JSON</b>"))
        assertTrue(html.contains("<pre>"))
        assertTrue(html.contains("ok"))
    }
}
