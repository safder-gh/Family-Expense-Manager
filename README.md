# Famly — Family Expense Manager

A family-scoped expense manager with an ASP.NET Core Web API, SQLite storage, and a React + TypeScript web app. It tracks virtual wallets only; no real payments are processed.

## What it does

- The first person registers a family and becomes its head.
- The head creates a single-use invite code (valid for 24 hours); dependents use it to create their own accounts and join the family.
- All members sign in with a JWT-protected account. Passwords are hashed by ASP.NET Core Identity, tokens expire after 30 minutes, and API access is scoped to the member's family and role.
- Wallets start at zero. The head records external funds added to their wallet, then can transfer funds to dependent wallets.
- Each member records expenses against their own wallet and selects a family category. Families start with Food, Travel, Health, and Other; any member can create, rename, and delete categories, but categories already used by an expense cannot be deleted.
- Transfers and expenses reject insufficient balances and are committed atomically with the ledger entry. Expense categories appear in transaction history.
- Family members can view wallet balances and up to 100 family transactions.

## Requirements

- .NET 10 SDK
- Node.js 20.19+ or 22.12+

## Configure and run

1. Configure a JWT signing secret for the API. Do not commit this value:

   ```powershell
   dotnet user-secrets init --project backend\FamilyExpense.Api
   dotnet user-secrets set "Jwt:Key" "replace-with-a-random-secret-at-least-32-bytes-long" --project backend\FamilyExpense.Api
   ```

   For a different environment, provide `Jwt__Key` as an environment variable. The API refuses to start if a sufficiently long key is missing.

2. Start the API from the repository root:

   ```powershell
   dotnet run --project backend\FamilyExpense.Api --urls http://localhost:5080
   ```

   On first startup, the API creates the SQLite database at `family-expenses.db` in the current working directory. Development OpenAPI is available at `http://localhost:5080/openapi/v1.json`.

3. In a second terminal, start the React app:

   ```powershell
   cd frontend
   npm install
   npm run dev
   ```

   Open the Vite URL (normally `http://localhost:5173`). Vite proxies `/api` requests to the local API at port 5080.

## Notes

- Monetary inputs are USD and stored as integer cents.
- A family invite's raw code is shown only when created; only its SHA-256 hash is stored.
- The browser keeps the access token in session storage and clears it when signing out or closing the tab.
- `EnsureCreated` initializes a new local database. Back up `family-expenses.db` if it contains data; do not delete it to reset the app.
- On startup, the API additively creates expense-category storage for existing databases and assigns earlier uncategorized expenses to each family's Other category.
- The local HTTP setup is for development. Use HTTPS, managed secrets, durable database migrations/backups, and operational hardening before deploying.
