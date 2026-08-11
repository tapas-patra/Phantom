// swift-tools-version: 5.9

import PackageDescription

let package = Package(
    name: "PhantomMac",
    platforms: [.macOS("12.3")],
    products: [
        .executable(name: "Phantom", targets: ["Phantom"])
    ],
    targets: [
        .executableTarget(
            name: "Phantom",
            path: "Sources/Phantom",
            linkerSettings: [
                .linkedFramework("AVFoundation"),
                .linkedFramework("Carbon"),
                .linkedFramework("Security"),
                .linkedFramework("Speech"),
                .linkedFramework("WebKit")
            ]
        )
    ],
    swiftLanguageVersions: [.v5]
)
