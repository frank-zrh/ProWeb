# ProWeb

A secure web proxy system built on .NET 8 that provides encrypted, session-based content fetching with anti-replay protection and intelligent caching.

## Overview

ProWeb is a client-server architecture designed to securely proxy web content with end-to-end encryption. It consists of a server component that handles content fetching and a Windows WPF client application with an embedded Chromium browser.

## Architecture

The project is organized into the following components:

- **ProWeb.Server** - ASP.NET Core server that handles proxy requests with HTTPS/HTTP3 support
- **ProWeb.Client** - Windows WPF desktop client with CefSharp-based browser
- **ProWeb.Client.Core** - Core client logic and business rules
- **ProWeb.Shared** - Shared libraries for cryptography, content rewriting, and protocol definitions

## Key Features

### Security
- **End-to-End Encryption**: X25519 ECDH key exchange with HKDF-derived AES-256 session keys
- **Anti-Replay Protection**: Request timestamps and nonce validation
- **JWT-based Authentication**: Secure session tokens with configurable expiration
- **Session Management**: Protected session storage with master key encryption
- **Mutual TLS Support**: Optional client certificate validation
- **HSTS**: Strict Transport Security headers for enforced HTTPS

### Performance
- **Intelligent Caching**: Configurable TTL-based content caching with SQLite storage
- **Rate Limiting**: Built-in request throttling
- **HTTP/3 Support**: QUIC protocol support for reduced latency
- **Headless Browser Fallback**: PuppeteerSharp integration for JavaScript-heavy sites

### Content Fetching
- **Dual Fetcher Strategy**: 
  - `HttpClientFetcher` - Fast HTTP client for standard requests
  - `HeadlessBrowserFetcher` - PuppeteerSharp for dynamic content rendering
- **Content Rewriting**: URL and content transformation for proxied responses
- **Cookie Management**: Session-based cookie persistence
- **Encoding Detection**: Automatic charset detection and handling

### Observability
- **Structured Logging**: Serilog integration with file and console output
- **Request Logging**: Complete audit trail of proxy requests
- **Sensitive Data Redaction**: Automatic PII protection in logs
- **Request ID Tracking**: Correlation IDs for distributed tracing

## Technology Stack

### Server
- .NET 8.0
- ASP.NET Core with Kestrel
- Microsoft.Data.Sqlite
- PuppeteerSharp (headless browser automation)
- Serilog (structured logging)
- Polly (resilience and transient-fault handling)
- JWT Bearer authentication
- StyleCop.Analyzers (code quality)

### Client
- .NET 8.0 (Windows)
- WPF (Windows Presentation Foundation)
- CefSharp.Wpf.NETCore (Chromium Embedded Framework)
- Serilog

## Getting Started

### Prerequisites
- .NET 8.0 SDK
- Windows OS (for client application)
- SQLite (included via NuGet)

### Configuration

The server is configured via `appsettings.json` with the following key sections:

```json
{
  "ProWeb": {
    "Server": {
      "HttpsPort": 8443,
      "UseHttps": true,
      "EnableHsts": true,
      "RequireClientCertificate": false,
      "EnableHttp3": false
    },
    "Jwt": {
      "Issuer": "proweb",
      "Audience": "proweb-client",
      "SigningKey": "<your-secure-key>"
    },
    "Session": {
      "MasterKey": "<your-secure-key>"
    },
    "Fetch": {
      "TimeoutSeconds": 30
    },
    "Storage": {
      "DatabasePath": "proweb.db",
      "CacheTtlSeconds": 3600
    }
  }
}
```

### Production Security Requirements

⚠️ **IMPORTANT**: The server will refuse to start in Production mode with default/placeholder secrets. You must override `Jwt.SigningKey` and `Session.MasterKey` via:
- Configuration files
- Environment variables
- Azure Key Vault or other secure configuration providers

### Running the Server

```bash
cd src/ProWeb.Server
dotnet run
```

The server will start on the configured HTTPS port (default: 8443).

### Running the Client

```bash
cd src/ProWeb.Client
dotnet run
```

The WPF application will launch with an embedded Chromium browser configured to use the ProWeb proxy.

## Development

### Building

```bash
dotnet build
```

### Running Tests

```bash
dotnet test
```

Test projects include:
- `ProWeb.Server.Tests`
- `ProWeb.Client.Core.Tests`
- `ProWeb.Shared.Tests`

## Project Structure

```
├── src/
│   ├── ProWeb.Server/          # Server application
│   │   ├── Auth/               # JWT and session authentication
│   │   ├── Config/             # Configuration models
│   │   ├── Endpoints/          # HTTP endpoints and services
│   │   ├── Fetching/           # Content fetching strategies
│   │   ├── Middleware/         # ASP.NET Core middleware
│   │   ├── Observability/      # Logging and monitoring
│   │   └── Storage/            # SQLite repositories
│   ├── ProWeb.Client/          # WPF desktop client
│   ├── ProWeb.Client.Core/     # Client business logic
│   └── ProWeb.Shared/          # Shared libraries
│       ├── Content/            # Content processing
│       ├── Crypto/             # Cryptographic services
│       ├── Protocol/           # Envelope definitions
│       └── Serialization/      # Data serialization
└── tests/                      # Test projects
```

## Protocol

ProWeb uses a custom encrypted envelope protocol:

1. **Handshake**: Client initiates session with X25519 public key
2. **Key Exchange**: Server responds with its public key and JWT
3. **Session Key Derivation**: Both parties derive AES-256 key via HKDF
4. **Encrypted Requests**: All proxy requests are encrypted with session key
5. **Encrypted Responses**: Server responses are encrypted before transmission

## License

*Add your license information here*

## Contributing

*Add contribution guidelines here*

## Support

*Add support contact information here*
