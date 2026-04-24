# Indians in Pittsburgh – Lightweight Architecture & Tech Stack

## 🎯 Goal
Build a very low-cost, fast, static-first website with minimal backend usage.

---

## 🧱 Architecture Overview

Static HTML (Frontend)
↓
Azure Functions (Write APIs)
↓
Azure Table Storage (Data)
↓
Queue (Event Trigger)
↓
Page Generator Function
↓
Azure Blob Storage (Generated HTML)

---

## ⚙️ Core Design Principles
- No SSR
- Static pages only
- Eventual consistency (~1 min)
- Event-driven regeneration
- Minimal cost

---

## 🌐 Frontend
- Static HTML + Alpine.js
- Tailwind / Bootstrap
- Hosted on Azure Static Web Apps

---

## ⚙️ Backend APIs
- Azure Functions (Consumption)
- Validate input
- Save data
- Push queue events

---

## 🗄️ Database
- Azure Table Storage
- Cheap and simple key-value storage

---

## 📎 File Storage
- Azure Blob Storage
- Store PDFs and attachments

---

## 🔁 Event Processing
- Azure Queue Storage

Flow:
1. Save data
2. Push queue message
3. Trigger generator

---

## 🧠 Page Generation
- Queue-triggered Azure Function
- Regenerate only affected pages

---

## 📄 Static Pages
Stored in Blob:
/events/index.html
/businesses/index.html
/index.html

---

## 🎨 Templates
- Scriban or simple string templates

---

## ⚡ Performance
- CDN via Static Web Apps
- Cache-Control headers

---

## 💰 Cost Estimate
Total: ~$2–$10/month

---

## 🚀 Summary
Ultra-lightweight, low-cost, scalable architecture ideal for community websites.
