-- Run this script while authenticated as the Microsoft Entra SQL administrator.
-- Replace the principal name with the value of the database_principal_name Terraform variable.
-- The App Service managed identity cannot access the database until this is applied.

CREATE USER [<APP_SERVICE_NAME>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [<APP_SERVICE_NAME>];

-- Keep write access disabled unless the gateway genuinely needs it.
-- ALTER ROLE db_datawriter ADD MEMBER [app-enterprise-ai-gateway-prod];