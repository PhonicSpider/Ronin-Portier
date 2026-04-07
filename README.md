# Ronin Portier 🏯
**A lightweight, open-source Windows Firewall manager for easily opening and closing ports as needed.**

Ronin Portier was built to simplify the process of opening, closing and managing firewall rules for admins, specifically tailored for ease of use and little overhead. It allows you to save server profiles, manage port ranges, and open or close ports ports at the click of a button.

While this can be used and was primarily built to simplify the set-up for game servers, it can just as easily be used for any application that requires specific ports to be opened on the firewall, such as web servers, database servers, or any custom applications.

---

## 🚀 Quick Start
1. **Download:** Go to the [Releases](https://github.com/PhonicSpider/Ronin-Portier/releases/tag/Release) tab and download the latest `Ronin_Portier.zip`.
2. **Extract:** Unzip the contents to a folder of your choice.
3. **Run as Admin:** `Ronin_Portier.exe` will ask to run as administrator. Click **YES** and enjoy!..
    * *Note: Administrative privileges are required to modify Windows Firewall rules.*

---

## 🛠 Features
* **Profile Management:** Save and load server configurations via JSON.
* **Smart Port Entry:** Supports single ports (`27016`), comma-separated lists (`27016,27017`), and ranges (`27015-27030`).
* **Protocol Support:** Toggle TCP, UDP, or both simultaneously.
* **Clean UI:** Modern dark-themed interface with real-time console logging.
* **One-Click Cleanup:** Easily remove all firewall rules associated with a specific server profile.
* **Console Logging:** View real-time logs of actions taken, including rule creation and deletion.
---

## 📖 How to Use
1. **Enter Server Name:** Type the name of your server (e.g., "SE-Crossplay-Alpha").
2. **Define Ports:** Enter the ports required by your game server.
3. **Select Protocols:** Check TCP, UDP, or both.
4. **Apply:** Click **APPLY** to create the inbound rules.
5. **Remove:** To clean up, select the server from the dropdown and click **REMOVE**.

---

## 💻 Contributing
Community contributions are welcome! If you have ideas for new features (like auto-detecting running processes or remote server management), feel free to:
1. **Fork** the project.
2. Create your **Feature Branch** (`git checkout -b feature/AmazingFeature`).
3. **Commit** your changes (`git commit -m 'Add some AmazingFeature'`).
4. **Push** to the Branch (`git push origin feature/AmazingFeature`).
5. Open a **Pull Request**.
6. Discuss and collaborate on your changes with the community.

---

## ⚖️ License
This project is licensed under the **GNU General Public License v3.0**.

**What this means for you:**
* **Permissions:** You are free to download, modify, and redistribute this software.
* **Conditions:** If you distribute a modified version of this software, you **must** also make your source code available under the GPLv3.
* **Warranty:** This software is provided "as is," without warranty of any kind. 

For the full legal text, please see the [LICENSE](./LICENSE) file included in this repository.
