import AppKit
import CoreImage
import CoreImage.CIFilterBuiltins

/// Generates an NSImage rendering of a QR code using the built-in CoreImage filter.
/// No external dependencies; available on macOS 10.15+.
enum QrCodeGenerator {
    /// Returns an NSImage for the given string, or nil if the filter fails.
    /// `pixelsPerModule` controls the rendered resolution (crisp on Retina).
    static func image(from string: String, pixelsPerModule: CGFloat = 10) -> NSImage? {
        guard !string.isEmpty else { return nil }
        let filter = CIFilter.qrCodeGenerator()
        filter.message = Data(string.utf8)
        filter.correctionLevel = "M"
        guard let output = filter.outputImage else { return nil }
        let scaled = output.transformed(by: CGAffineTransform(scaleX: pixelsPerModule, y: pixelsPerModule))
        let rep = NSCIImageRep(ciImage: scaled)
        let image = NSImage(size: scaled.extent.size)
        image.addRepresentation(rep)
        return image
    }
}
