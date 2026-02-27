const http = require("node:http");
const crypto = require("node:crypto");
const { classifyIntent, INTENTS } = require("./intent-classifier");
const { isMSBuildRelated } = require("./domain-check");
const fs = require("node:fs");
const path = require("node:path");

const PORT = process.env.PORT || 3000;

// Load compiled knowledge files at startup
const knowledgeDir = path.join(__dirname, "knowledge");
const knowledge = {};

function loadKnowledge() {
  if (!fs.existsSync(knowledgeDir)) {
    console.warn(
      "Knowledge directory not found. Run 'npm run compile-knowledge' first."
    );
    return;
  }
  for (const file of fs.readdirSync(knowledgeDir)) {
    if (file.endsWith(".lock.md")) {
      const key = path.basename(file, ".lock.md");
      knowledge[key] = fs.readFileSync(path.join(knowledgeDir, file), "utf-8");
    }
  }
  console.log(`Loaded knowledge areas: ${Object.keys(knowledge).join(", ")}`);
}

// Verify GitHub webhook signature
function verifySignature(payload, signature) {
  const secret = process.env.GITHUB_WEBHOOK_SECRET;
  if (!secret) return true; // Skip verification in development
  if (!signature) return false;

  const sig = crypto
    .createHmac("sha256", secret)
    .update(payload)
    .digest("hex");
  const expected = `sha256=${sig}`;
  return crypto.timingSafeEqual(
    Buffer.from(signature),
    Buffer.from(expected)
  );
}

// Build the system prompt with relevant knowledge context
function buildSystemPrompt(intent, userMessage) {
  const basePrompt = `You are the MSBuild Expert — a specialized assistant for MSBuild, .NET SDK builds, and project file best practices.

Your areas of expertise:
- Build failure diagnosis (CS, MSB, NU, NETSDK error codes)
- Source generator and analyzer issues (CS8785, AD0001)
- Build performance optimization (incremental builds, parallelism, graph builds)
- MSBuild project file quality (style guide, anti-patterns, modernization)
- NuGet package management and Central Package Management
- Directory.Build.props/targets organization
- Multi-targeting and TFM compatibility

Guidelines:
- Be concise and actionable — provide specific commands, XML snippets, and step-by-step fixes
- When suggesting fixes, show BAD → GOOD transformations
- Reference specific error codes and MSBuild properties by name
- Suggest binary log analysis (dotnet build /bl) for complex issues
- If the question is outside MSBuild/.NET build scope, say so briefly and suggest general Copilot instead`;

  // Append relevant knowledge based on intent
  const knowledgeKey = INTENTS[intent]?.knowledgeKey;
  if (knowledgeKey && knowledge[knowledgeKey]) {
    return `${basePrompt}\n\n## Reference Knowledge\n\n${knowledge[knowledgeKey]}`;
  }

  return basePrompt;
}

// Handle incoming Copilot Extension requests
async function handleCopilotRequest(req, res) {
  let body = "";
  req.on("data", (chunk) => {
    body += chunk;
  });

  req.on("end", () => {
    // Verify webhook signature
    const signature = req.headers["x-hub-signature-256"];
    if (!verifySignature(body, signature)) {
      res.writeHead(401, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ error: "Invalid signature" }));
      return;
    }

    let payload;
    try {
      payload = JSON.parse(body);
    } catch {
      res.writeHead(400, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ error: "Invalid JSON" }));
      return;
    }

    // Extract the user's message from the Copilot payload
    const messages = payload.messages || [];
    const lastUserMessage = messages
      .filter((m) => m.role === "user")
      .pop();

    if (!lastUserMessage) {
      res.writeHead(400, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ error: "No user message found" }));
      return;
    }

    const userMessage = lastUserMessage.content;

    // Domain check — is this MSBuild-related?
    if (!isMSBuildRelated(userMessage)) {
      // Return a polite redirect response
      const response = {
        messages: [
          {
            role: "system",
            content:
              "The user's question doesn't appear to be related to MSBuild or .NET builds. Politely explain that @msbuild specializes in MSBuild/.NET build topics and suggest they ask Copilot directly for general programming help.",
          },
          ...messages,
        ],
      };
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify(response));
      return;
    }

    // Classify intent and build knowledge-augmented response
    const intent = classifyIntent(userMessage);
    const systemPrompt = buildSystemPrompt(intent, userMessage);

    // Return the augmented message list with system context
    const response = {
      messages: [
        {
          role: "system",
          content: systemPrompt,
        },
        ...messages,
      ],
    };

    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify(response));
  });
}

// Simple HTTP server
const server = http.createServer((req, res) => {
  // Health check
  if (req.method === "GET" && req.url === "/health") {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(
      JSON.stringify({
        status: "ok",
        knowledgeAreas: Object.keys(knowledge),
      })
    );
    return;
  }

  // Copilot Extension endpoint
  if (req.method === "POST" && req.url === "/api/copilot") {
    handleCopilotRequest(req, res);
    return;
  }

  res.writeHead(404, { "Content-Type": "application/json" });
  res.end(JSON.stringify({ error: "Not found" }));
});

// Start
loadKnowledge();
server.listen(PORT, () => {
  console.log(`MSBuild Copilot Extension listening on port ${PORT}`);
});

module.exports = { handleCopilotRequest, buildSystemPrompt, loadKnowledge };
