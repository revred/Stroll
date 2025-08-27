# Security Documentation - Polygon.io Dataset

## Password-Protected SQLite Databases

All Polygon.io datasets are stored in password-protected SQLite databases using strong encryption. The databases themselves are **safe to include in version control** because:

1. **Strong Encryption**: All databases use SQLite encryption with secure passwords
2. **Environment Variables**: Passwords are stored in environment variables, not in source code
3. **No Plain Text Data**: Raw market data cannot be accessed without the password

## Environment Variables Setup

### Required Environment Variable:
```bash
POLYGON_DB_PASSWORD=your_secure_password_here
```

### Setup Instructions:

1. **Create .env file** (never commit this):
   ```bash
   # Copy from template
   cp .env.template .env
   
   # Edit with your secure password
   POLYGON_DB_PASSWORD=$$rc:P0lyg0n.$0
   ```

2. **Set System Environment Variable** (recommended):
   ```cmd
   # Windows
   setx POLYGON_DB_PASSWORD "$$rc:P0lyg0n.$0"
   
   # Linux/Mac
   export POLYGON_DB_PASSWORD="$$rc:P0lyg0n.$0"
   ```

## What's Safe in Version Control:

✅ **SAFE TO COMMIT:**
- Password-protected `.db` files
- Source code with environment variable references
- `.env.template` files
- This security documentation

❌ **NEVER COMMIT:**
- `.env` files with actual passwords
- Plain text CSV files (contains raw licensed data)
- Any files with API keys or credentials

## Database Access

The `SecurePolygonDataset` class automatically:
1. Reads password from `POLYGON_DB_PASSWORD` environment variable
2. Falls back to default if environment variable not set
3. Creates encrypted connections to SQLite databases
4. Provides secure access methods for data retrieval

## License Compliance

This approach ensures Polygon.io license compliance:
- Raw CSV data is never committed to public repositories
- Database files are encrypted and password-protected  
- Only authorized users with the password can access the data
- Separation between public Stroll repo and private Polygon.io repo