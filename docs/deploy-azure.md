# Deploying to Azure (M4, manual)

This guide creates everything by hand with the Azure CLI, one resource at a time, so each piece is understood
before M5 turns it into Bicep + GitHub Actions. Commands are for **PowerShell**.

## What we're building

```
Browser ──HTTPS──> Azure Container Apps (ca-juntos)   ← image from ghcr.io (public)
                     │  user-assigned managed identity (id-juntos), no passwords anywhere
                     ├──> Azure SQL Database (free offer, serverless, Entra-only auth)
                     ├──> Key Vault: Google client secret + Data Protection key
                     └──> Google Calendar / Tasks APIs
Logs ──> Log Analytics (daily cap 0.1 GB)
```

| Resource | Why | Expected cost |
|---|---|---|
| Container Apps (consumption, 0–1 replica, 0.25 vCPU) | Runs the container; scales to zero when idle | Within the monthly free grant |
| Azure SQL Database (free offer, auto-pause on limit) | Users, groups, encrypted refresh tokens | Free (100k vCore-seconds/month) |
| Key Vault (standard) | Google client secret + key that wraps the Data Protection keys | Cents |
| Managed identity | The app's identity for SQL and Key Vault | Free |
| Log Analytics (0.1 GB/day cap) | Container logs | Within the free 5 GB/month |
| Image on ghcr.io | Container registry | Free (public package) |

> Prices change: check the [Azure Pricing Calculator](https://azure.microsoft.com/pricing/calculator/).
> **Step 1 (budget alert) comes before any resource.**

## 0. Prerequisites

```powershell
winget install Microsoft.AzureCLI
```

Open a **new** terminal (to refresh PATH), then sign in and check the subscription:

```powershell
az login
az account show --query "{name:name, id:id}" -o table
az provider register --namespace Microsoft.App
az provider register --namespace Microsoft.OperationalInsights
```

## 1. Budget alert (portal)

**Cost Management → Budgets → Add**: monthly, amount **R$ 50** (or the equivalent in your billing currency),
alerts at **50 %, 80 % and 100 %** to your e-mail. A budget **alerts**, it doesn't stop charges.

## 2. Variables

Key Vault and SQL server names are global: the suffix makes them unique.

```powershell
$SUFFIX   = -join ((97..122) | Get-Random -Count 5 | ForEach-Object { [char]$_ })
$RG       = "rg-juntos"
$LOC      = "brazilsouth"          # closest region to Belo Horizonte
$ID_NAME  = "id-juntos"
$KV       = "kv-juntos-$SUFFIX"
$SQL      = "sql-juntos-$SUFFIX"
$DB       = "juntos"
$LOG      = "log-juntos"
$CAE      = "cae-juntos"
$APP      = "ca-juntos"
$IMAGE    = "ghcr.io/c4ldeira27/juntos:v1"
$ME_ID    = az ad signed-in-user show --query id -o tsv
$ME_UPN   = az ad signed-in-user show --query userPrincipalName -o tsv
"Suffix: $SUFFIX   (write it down: you need it if you open a new terminal)"
```

## 3. Resource group and managed identity

```powershell
az group create -n $RG -l $LOC

az identity create -n $ID_NAME -g $RG -l $LOC
$ID_RESOURCE  = az identity show -n $ID_NAME -g $RG --query id -o tsv
$ID_CLIENT    = az identity show -n $ID_NAME -g $RG --query clientId -o tsv
$ID_PRINCIPAL = az identity show -n $ID_NAME -g $RG --query principalId -o tsv
```

A **user-assigned** identity exists on its own, so we can give it permissions *before* the app exists
(a system-assigned one would only be born together with the app).

## 4. Key Vault

```powershell
az keyvault create -n $KV -g $RG -l $LOC --enable-rbac-authorization true
$KV_ID = az keyvault show -n $KV -g $RG --query id -o tsv

# With RBAC, even the vault's creator has no access to secrets/keys until a role is assigned.
az role assignment create --role "Key Vault Administrator" --assignee $ME_ID --scope $KV_ID

# The app may read secrets and use keys to wrap/unwrap — nothing else (least privilege).
az role assignment create --role "Key Vault Secrets User" --assignee-object-id $ID_PRINCIPAL --assignee-principal-type ServicePrincipal --scope $KV_ID
az role assignment create --role "Key Vault Crypto User"  --assignee-object-id $ID_PRINCIPAL --assignee-principal-type ServicePrincipal --scope $KV_ID
```

Role assignments take a minute or two to propagate. Then:

```powershell
az keyvault key create --vault-name $KV -n dataprotection --kty RSA --size 2048
$DP_KEY_ID = az keyvault key show --vault-name $KV -n dataprotection --query key.kid -o tsv

# Type the Google client secret yourself (it is not written anywhere else):
$GOOGLE_SECRET = Read-Host "Google client secret" -AsSecureString
az keyvault secret set --vault-name $KV -n google-client-secret --value ([System.Net.NetworkCredential]::new('', $GOOGLE_SECRET).Password) --query id -o tsv
$GOOGLE_SECRET_URI = "https://$KV.vault.azure.net/secrets/google-client-secret"
```

The secret URI has no version on purpose: Container Apps picks up the latest version.

## 5. Azure SQL (free offer, Entra-only)

```powershell
# Entra-only authentication: no SQL logins/passwords exist at all. You are the admin.
az sql server create -n $SQL -g $RG -l $LOC --enable-ad-only-auth `
  --external-admin-principal-type User --external-admin-name $ME_UPN --external-admin-sid $ME_ID

# Lets Azure services (our container) reach the server. Authentication still requires an Entra identity.
az sql server firewall-rule create -g $RG -s $SQL -n AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0

# Free offer: serverless, and when the monthly free limit runs out it PAUSES instead of billing.
az sql db create -g $RG -s $SQL -n $DB --edition GeneralPurpose --compute-model Serverless `
  --family Gen5 --capacity 2 --use-free-limit --free-limit-exhaustion-behavior AutoPause
```

> If the CLI rejects `--use-free-limit`, create the database in the portal instead: the "Azure SQL Database
> free offer" banner on the create page. Pick **Auto-pause the database until next month**.

**Create the app's database user** (portal → SQL database `juntos` → **Query editor** → sign in with
Entra ID; allow your IP when it asks):

```sql
CREATE USER [id-juntos] FROM EXTERNAL PROVIDER;
ALTER ROLE db_owner ADD MEMBER [id-juntos];
```

`db_owner` because the app runs the migrations on startup in M4. In M5 migrations move to the pipeline and the
app drops to `db_datareader` + `db_datawriter`.

## 6. Container image on ghcr.io

```powershell
gh auth refresh -s write:packages
gh auth token | docker login ghcr.io -u c4ldeira27 --password-stdin
docker build -t $IMAGE .
docker push $IMAGE
```

Then on GitHub: **your profile → Packages → juntos → Package settings → Change visibility → Public**.
The image holds no secrets (all configuration comes from environment variables and Key Vault), and a public
package lets Container Apps pull it without registry credentials.

## 7. Log Analytics and the Container Apps environment

```powershell
# --quota is the DAILY ingestion cap in GB: a forgotten Debug log can't turn into a bill.
az monitor log-analytics workspace create -g $RG -n $LOG -l $LOC --quota 0.1
$LOG_ID  = az monitor log-analytics workspace show -g $RG -n $LOG --query customerId -o tsv
$LOG_KEY = az monitor log-analytics workspace get-shared-keys -g $RG -n $LOG --query primarySharedKey -o tsv

az containerapp env create -n $CAE -g $RG -l $LOC --logs-workspace-id $LOG_ID --logs-workspace-key $LOG_KEY
```

## 8. The container app

```powershell
$CONN = "Server=tcp:$SQL.database.windows.net,1433;Database=$DB;Authentication=Active Directory Managed Identity;User Id=$ID_CLIENT;Encrypt=True;Connect Timeout=60"
$GOOGLE_CLIENT_ID = Read-Host "Google client id"

az containerapp create -n $APP -g $RG --environment $CAE --image $IMAGE `
  --target-port 8080 --ingress external `
  --min-replicas 0 --max-replicas 1 --cpu 0.25 --memory 0.5Gi `
  --user-assigned $ID_RESOURCE `
  --secrets "google-client-secret=keyvaultref:$GOOGLE_SECRET_URI,identityref:$ID_RESOURCE" `
  --env-vars `
    "AZURE_CLIENT_ID=$ID_CLIENT" `
    "ConnectionStrings__Default=$CONN" `
    "Google__ClientId=$GOOGLE_CLIENT_ID" `
    "Google__ClientSecret=secretref:google-client-secret" `
    "DataProtection__KeyVaultKeyId=$DP_KEY_ID" `
    "Database__MigrateOnStartup=true"

$FQDN = az containerapp show -n $APP -g $RG --query properties.configuration.ingress.fqdn -o tsv
"https://$FQDN"
```

What each setting does:

- `--min-replicas 0`: scale to zero when idle (free), at the cost of a cold start of several seconds.
- `--max-replicas 1`: required while migrations run on startup.
- `AZURE_CLIENT_ID`: tells `DefaultAzureCredential` (Key Vault) which identity to use.
  The SQL connection string names the same identity in `User Id`.
- `secretref:` / `keyvaultref:`: the Google secret is read from Key Vault by the platform; it never appears
  in the app's configuration or in the CLI history.

## 9. Google OAuth redirect URI

Google Cloud → **Google Auth Platform → Clients → Juntos (dev)** → add to *Authorized redirect URIs*:

```
https://<FQDN>/signin-google
```

(Or create a separate "Juntos (prod)" client — then use its id/secret in steps 4 and 8.)
The app stays in *Testing* until there's a custom domain, so add every user under *Test users*.

## 10. Check it

Open `https://<FQDN>`. The first request after idle takes a while (container start + database resume).

```powershell
az containerapp logs show -n $APP -g $RG --follow     # app logs (stdout)
az containerapp revision list -n $APP -g $RG -o table # revisions and their state
```

Common problems:

| Symptom | Likely cause |
|---|---|
| Container restarts in a loop, log says Google settings missing | `Google__ClientId`/secret not set or Key Vault reference failing (identity role not propagated yet: wait and restart the revision) |
| `Login failed for user '<token-identified principal>'` | The `CREATE USER [id-juntos] FROM EXTERNAL PROVIDER` step wasn't run |
| `redirect_uri_mismatch` | Step 9 missing, or the URI isn't exactly `https://<FQDN>/signin-google` |
| Key Vault `Forbidden` | Role assignment not propagated yet, or `AZURE_CLIENT_ID` missing |

## Updating the app

```powershell
$IMAGE = "ghcr.io/c4ldeira27/juntos:v2"
docker build -t $IMAGE .
docker push $IMAGE
az containerapp update -n $APP -g $RG --image $IMAGE
```

Every update creates a new **revision**; if it fails to start, traffic stays on the previous one.

## Tearing everything down

```powershell
az group delete -n $RG --yes
```

Deletes every resource of the project at once (that's what the resource group is for).
Key Vault keeps a soft-deleted copy for 90 days; `az keyvault purge -n $KV` removes it for good.
