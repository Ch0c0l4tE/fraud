# Makefile for running all components of the Fraud Detection project

# Variables
API_DIR=src/Fraud.Ingestion.Api
PLAYGROUND_DIR=tools/playground
SDK_DIR=src/sdks/browser

.PHONY: all api playground sdk build clean

# Run all components
all: api playground

# Run the API
api:
	cd $(API_DIR) && dotnet run

# Run the Playground
playground:
	cd $(PLAYGROUND_DIR) && npm run dev

# Build the SDK
sdk:
	cd $(SDK_DIR) && npm run build

# Build all components
build:
	dotnet build
	cd $(SDK_DIR) && npm run build

# Clean all build artifacts
clean:
	dotnet clean
	cd $(SDK_DIR) && npm run clean