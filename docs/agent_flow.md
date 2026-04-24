# Agent Intent & Workflow Architecture

To support the robust, multi-step agent flow, we will transition our Azure Function from a simple "extract and save" script into an **Intent-Based State Machine**.

## The Unified Gemini Payload
Currently, Gemini only returns an `EventDto`. We will upgrade `GeminiService.cs` so that Gemini returns an `AgentResponse` JSON object:
```json
{
  "Intent": "ASK_TYPE | VALIDATE_EVENT | VALIDATE_BUSINESS | GENERATE_IMAGE | INSTRUCTIONS",
  "IsComplete": true/false,
  "Summary": "Short summary of what the user sent...",
  "UserMessage": "The exact message you want to show the user (e.g. 'You are missing the event date!').",
  "EventDetails": { ... },
  "BusinessDetails": { ... }
}
```

## Workflows

### 1. Unassigned / Initial Message
**Trigger:** User sends text or image (no tags, no callback buttons).
**Action:** Gemini is instructed to read the input. 
- If conversational (e.g., "Hi"): `Intent = INSTRUCTIONS`. Returns a welcoming help message.
- If it contains data: `Intent = ASK_TYPE`. Returns a summary.
**Telegram Response:** 
"Here is a summary: *[Summary]*... What type of post is this?"
[Event Button] [Business Button] [Home Button]

### 2. Home Landing Page
**Trigger:** User clicks [Home Button] or message contains `<<Home>>`.
**Action:**
- **Text Only:** `Intent = GENERATE_IMAGE`. (Feature: We can use Gemini or Google Imagen to generate an image based on the text!). 
  *Note: If we can't generate images easily, we can ask the user to provide an image instead.*
- **Image Provided:** We upload the image, save it to the `LandingImages` Azure Table, and return `[Saved Home] EditCode`.

### 3. Event Processing (`<<Event>>`)
**Trigger:** User clicks [Event Button] or message contains `<<Event>>`.
**Action:** We tell Gemini: "You MUST extract an Event. Required fields: Heading, Description, Location, Date. If anything is missing, set IsComplete=false and list them in UserMessage."
- **If Complete:** Save to `Events` Azure Table, return `[Saved Event] EditCode`.
- **If Incomplete:** We send a Telegram message containing the exact tag and instructions:
   > ❌ You are missing the Date and Location.
   > Please copy the text below, fill in the missing details, and reply:
   > \<\<Event\>\>
   > ImageLink: https://...
   > *[Summary / Extracted Data]*

### 4. Business Processing (`<<Business>>`)
**Trigger:** User clicks [Business Button] or message contains `<<Business>>`.
**Action:** We tell Gemini: "You MUST extract a Business. Required fields: Name, Address, Phone."
- **If Complete:** Save to `Businesses` Azure Table, return `[Saved Business] EditCode`.
- **If Incomplete:** Send the fallback message with the `<<Business>>` tag as exactly templated above.

## Technical Implementation Steps
1. Create new models: `BusinessDto`, `AgentResponseDto`.
2. Rewrite `ExtractEventAsync` into a unified `ProcessRequestAsync(text, image, expectedType)` method.
3. Update `TelegramWebhook.cs` to intercept `<<Event>>`, `<<Business>>`, `<<Home>>` tags from the raw text loop to determine `expectedType`.
4. Implement the Callback Query logic for the 3 selection buttons.
