# 1. Define Variables (feel free to change the names, but they must be unique)
RG_NAME="rg-burghindian"
LOCATION="eastus"
STORAGE_ACCOUNT="stburghindian$RANDOM" # Storage accounts must be globally unique
FUNC_APP_NAME="func-burghindian-api"

# 2. Create the Resource Group
az group create --name $RG_NAME --location $LOCATION

# 3. Create the Storage Account (Required for Functions)
az storage account create --name $STORAGE_ACCOUNT --location $LOCATION --resource-group $RG_NAME --sku Standard_LRS

# 4. Create the Function App (.NET isolated mode, v4)
az functionapp create \
  --resource-group $RG_NAME \
  --consumption-plan-location $LOCATION \
  --runtime dotnet-isolated \
  --functions-version 4 \
  --name $FUNC_APP_NAME \
  --storage-account $STORAGE_ACCOUNT

# 5. Configure the Environment Variables (Replace the placeholders with your actual keys)
az functionapp config appsettings set \
  --name $FUNC_APP_NAME \
  --resource-group $RG_NAME \
  --settings "TELEGRAM_BOT_TOKEN=YOUR_TELEGRAM_BOT_TOKEN_HERE" "GEMINI_API_KEY=YOUR_GEMINI_API_KEY_HERE"


# 6. Deploy the function app
az functionapp deployment source config-zip --resource-group rg-burghindian --name func-burghindian-api --src "/home/tejasvi/api-functions-Release.zip"

# 7. List the function app
az functionapp function list \
  --resource-group rg-burghindian \
  --name func-burghindian-api \
  --query "[].{Name:name, State:config.disabled}" \
  --output table

 # 8. Test the function app
 curl -X GET "https://func-burghindian-api.azurewebsites.net/api/Ping" 

 # 9. Get the Function Key
FUNC_KEY=$(az functionapp function keys list \
  --resource-group rg-burghindian \
  --name func-burghindian-api \
  --function-name TelegramWebhook \
  --query "default" -o tsv)
# 10. Build the secured URL
WEBHOOK_URL="https://func-burghindian-api.azurewebsites.net/api/telegram/webhook?code=$FUNC_KEY"
# 11. Register the Webhook
curl -X POST "https://api.telegram.org/bot<YOUR_TELEGRAM_BOT_TOKEN>/setWebhook" \
     -d "url=$WEBHOOK_URL"

# 12 List available models
curl -X GET "https://generativelanguage.googleapis.com/v1beta/models?key=<YOUR_GEMINI_API_KEY>"

