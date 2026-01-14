// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "FraudTracker",
    platforms: [
        .iOS(.v14),
        .macOS(.v12)
    ],
    products: [
        .library(
            name: "FraudTracker",
            targets: ["FraudTracker"]
        )
    ],
    dependencies: [],
    targets: [
        .target(
            name: "FraudTracker",
            dependencies: [],
            path: "Sources/FraudTracker"
        ),
        .testTarget(
            name: "FraudTrackerTests",
            dependencies: ["FraudTracker"],
            path: "Tests/FraudTrackerTests"
        )
    ]
)
