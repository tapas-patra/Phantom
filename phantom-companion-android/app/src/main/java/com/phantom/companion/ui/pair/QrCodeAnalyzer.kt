package com.phantom.companion.ui.pair

import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import com.google.zxing.BinaryBitmap
import com.google.zxing.MultiFormatReader
import com.google.zxing.NotFoundException
import com.google.zxing.PlanarYUVLuminanceSource
import com.google.zxing.common.HybridBinarizer

class QrCodeAnalyzer(
    private val onQrCodeScanned: (String) -> Unit
) : ImageAnalysis.Analyzer {

    private val reader = MultiFormatReader()
    private var isScanned = false

    override fun analyze(image: ImageProxy) {
        if (isScanned) {
            image.close()
            return
        }

        val buffer = image.planes[0].buffer
        val bytes = ByteArray(buffer.remaining())
        buffer.get(bytes)

        val width = image.width
        val height = image.height

        val source = PlanarYUVLuminanceSource(
            bytes,
            width,
            height,
            0,
            0,
            width,
            height,
            false
        )

        val binaryBitmap = BinaryBitmap(HybridBinarizer(source))

        try {
            val result = reader.decodeWithState(binaryBitmap)
            val text = result.text
            if (!text.isNullOrEmpty()) {
                isScanned = true
                onQrCodeScanned(text)
            }
        } catch (e: NotFoundException) {
            // No QR in frame
        } catch (e: Exception) {
            // Ignore other decode failures
        } finally {
            reader.reset()
            image.close()
        }
    }

    fun reset() {
        isScanned = false
    }
}
