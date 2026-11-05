# Family Expense Manager — Step-by-Step Plan

## Project goal

Build a family expense manager where a family head and invited dependents have separate accounts and virtual wallets. The head can record funds added from outside the app and transfer money to dependents. Each member can record their own expenses. The app must protect account access with JWT authentication, keep family data isolated, maintain an auditable transaction history, and prevent wallet balances from going below zero.

## Technology and scope

- **API:** ASP.NET Core Web API on .NET 10
- **UI:** React and TypeScript with Vite
- **Database:** SQLite for local development
- **Authentication:** ASP.NET Core Identity password hashing and 30-minute JWT access tokens
- **Money handling:** USD amounts stored as integer cents; virtual balances only, with no payment processor
- **Membership:** A head-created family and dependent self-registration through a single-use invite code that expires after 24 hours

## Step-by-step implementation plan

### 1. Establish the project structure — Done

1. Create a solution with separate API, API test, and React application projects.
2. Configure SQLite persistence and add the required .NET and npm dependencies.
3. Add repository ignores for build outputs, local databases, and installed packages.
4. Document local setup and startup in `README.md`.

**Result:** `backend/FamilyExpense.Api`, `backend/FamilyExpense.Api.Tests`, and `frontend` are established as the main project areas.

### 2. Implement account, family, and authentication flows — Done

1. Let a new head register with a display name, email, password, and family name.
2. Create the family head's zero-balance wallet and assign the `Head` role.
3. Let heads create cryptographically random invite codes that expire after 24 hours.
4. Store only a hash of each invite code; allow each invite to be claimed once.
5. Let a dependent register using an invite code, then create their family membership and zero-balance wallet.
6. Add login with generic invalid-credential responses.
7. Issue signed JWTs with identity, role, and family claims; require an adequately long secret from local secrets or environment configuration.

**Result:** Authentication endpoints are in `backend/FamilyExpense.Api/Controllers/AuthController.cs`; family and invite endpoints are in `backend/FamilyExpense.Api/Controllers/FamilyController.cs`.

### 3. Implement wallets and the transaction ledger — Done

1. Store each wallet balance in integer minor units to avoid floating-point rounding.
2. Let only the family head record an external top-up to the head's wallet.
3. Let only the head transfer available funds to a dependent in the same family.
4. Let each signed-in member record expenses against their own wallet.
5. Reject invalid monetary precision and any transfer or expense that exceeds the available balance.
6. Apply balance changes and their ledger records in the same database transaction.
7. Record the transaction type, amount, description, family, participants, creator, and timestamp.
8. Return family-scoped balances, members, and recent transaction history.

**Result:** Wallet endpoints are in `backend/FamilyExpense.Api/Controllers/WalletController.cs`; data entities and SQLite configuration are in `backend/FamilyExpense.Api/Models/FamilyEntities.cs` and `backend/FamilyExpense.Api/Data/FamilyDbContext.cs`.

### 4. Build the React user experience — Done

1. Provide sign-in, family creation, and invite-code join flows.
2. Store the access token in session storage and send it as a bearer token for API calls.
3. Show the current member's balance, family members, and recent activity.
4. Provide head-only controls for recording top-ups, creating invites, and transferring money.
5. Let all members enter expenses and display clear API errors and success feedback.
6. Configure Vite to proxy API requests to the local .NET server.

**Result:** The app screens are in `frontend/src/App.tsx`, with responsive styling in `frontend/src/App.css` and `frontend/src/index.css`.

### 5. Verify the core workflows — Done

1. Test head registration and successful/unsuccessful login.
2. Test invite creation, dependent registration, and rejection of invite reuse.
3. Test top-up, transfer, expense, resulting balances, and transaction history.
4. Verify that insufficient funds are rejected and do not create successful transactions.
5. Verify authenticated access and that one family's members are not exposed to another family.
6. Build and lint the frontend.

**Verification recorded:** `dotnet test backend\FamilyExpenseManager.slnx --no-restore`, `npm run build`, and `npm run lint` passed.

### 6. Prepare for continued development — Next

1. Replace `EnsureCreated` with versioned EF Core migrations before evolving the database schema.
2. Add tests for expired invites, malformed amounts, head/dependent authorization boundaries, concurrent spending, and transaction rollback.
3. Add browser-level end-to-end coverage for registration, login, family joining, and wallet forms.
4. Add account recovery, email verification, and a safer production token-refresh/revocation strategy if deployment requirements call for them.
5. Configure HTTPS, production CORS origins, managed signing secrets, database backups, and monitoring before public deployment.
6. Review accessibility, responsive layouts, and supported currency/localization requirements with users.

These are follow-up hardening and product tasks; they are not represented as completed features in the current version.

## Local development sequence

1. Configure a JWT secret using .NET user-secrets or the `Jwt__Key` environment variable; keep the secret out of source control.
2. Start the API from the project root:

   ```powershell
   dotnet run --project backend\FamilyExpense.Api --urls http://localhost:5080
   ```

3. In a separate terminal, install frontend packages and start Vite:

   ```powershell
   cd frontend
   npm install
   npm run dev
   ```

4. Open the Vite URL, create a family, record an external top-up, create an invite, join as a dependent, transfer funds, and record an expense.
5. Run the API tests and frontend checks after making changes:

   ```powershell
   dotnet test backend\FamilyExpenseManager.slnx --no-restore
   cd frontend
   npm run build
   npm run lint
   ```
