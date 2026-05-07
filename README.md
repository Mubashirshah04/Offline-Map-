# OMEGA Map Engine - Setup & User Guide

Welcome to the **OMEGA Map Engine**, a professional-grade offline mapping solution. This system allows you to harvest and visualize high-definition map data (Street, Satellite, ArcGIS, and Night Mode) with precision up to Zoom Level 22.

---

## 🛠️ Prerequisites

Ensure you have the following installed before starting:
*   **.NET 8.0 Core / Runtime**: [Download here](https://dotnet.microsoft.com/download/dotnet/8.0)
*   **PostgreSQL (v16+)**: [Download here](https://www.postgresql.org/download/)

---

## 🚀 Step 1: Database Setup

1.  Open **pgAdmin 4** or your preferred PostgreSQL tool.
2.  Create a database named: `omega_gis`.
3.  Execute the following SQL command to create the required table:

```sql
CREATE TABLE downloads (
    id SERIAL PRIMARY KEY,
    city VARCHAR(255) UNIQUE,
    status VARCHAR(50),
    size_mb DOUBLE PRECISION,
    completed_tiles BIGINT,
    total_tiles BIGINT,
    total_mb DOUBLE PRECISION,
    bbox_json TEXT
);
```

---

## 🏗️ Step 2: Backend Configuration

1.  Navigate to the `backend/` folder.
2.  Open `appsettings.json`.
3.  Update the **ConnectionStrings** with your PostgreSQL username and password:
    ```json
    "ConnectionStrings": {
      "PostgreSQLConnection": "Host=localhost;Database=omega_gis;Username=YOUR_USERNAME;Password=YOUR_PASSWORD"
    }
    ```
4.  Open a terminal in the `backend/` folder and run:
    ```bash
    dotnet run
    ```
    *Wait until you see "Application started" in the terminal.*

---

## 🌐 Step 3: Frontend Setup

1.  Open a **new** terminal in the `frontend/` folder.
2.  Install the required packages (one-time only):
    ```bash
    npm install
    ```
3.  Start the user interface:
    ```bash
    npm start
    ```
4.  The map will open automatically at: **http://localhost:4200**

---

## 💡 How to Use the System

4.  The system will start downloading 4 distinct layers:
    *   **Street**: Standard Google-style view.
    *   **Satellite**: High-definition aerial imagery.
    *   **ArcGIS**: Professional topographic data.
    *   **Night Mode**: Ultra-HD dark theme.

### 📊 Monitoring Progress
*   Check the **Status Bar** at the bottom for real-time Percentage, MBs, and Tile counts.
*   Use the **Pause** ⏸ or **Stop/Delete** ⏹ buttons as needed.
*   **Note**: Deleting a task instantly kills the background process and cleans up system resources.

### 🗺️ Offline Viewing
*   Once a download is complete (or partially finished), switch map layers using the control in the top-right corner.
*   Zoom in up to **Z22** for extreme HD detail.

---

## ⚠️ Important Notes
*   **Always keep the Backend terminal running** while using the map.
*   Large areas (like full countries) can take significant disk space and time.
*   The engine is optimized for **SSD storage** and **high-speed multi-threading**.

---
**OMEGA Map - Professional Edition**
*Powered by High-Performance GIS Architecture*
