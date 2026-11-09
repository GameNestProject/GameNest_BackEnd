# 🎮 GameNest Backend

![GameNest Banner]([https://via.placeholder.com/1000x250?text=GameNest+Backend+API](https://avatars.githubusercontent.com/u/341990373?s=200&v=4))

GameNest — це сучасний вебзастосунок для трекінгу ігор. Цей репозиторій містить серверну частину (BackEnd) проєкту, що забезпечує роботу з базою даних, авторизацію користувачів та бізнес-логіку управління особистими ігровими бібліотеками.

![.NET](https://img.shields.io/badge/.NET-5C2D91?style=for-the-badge&logo=.net&logoColor=white)
![C#](https://img.shields.io/badge/c%23-%23239120.svg?style=for-the-badge&logo=csharp&logoColor=white)
![MySQL](https://img.shields.io/badge/mysql-4479A1.svg?style=for-the-badge&logo=mysql&logoColor=white)

## 🛠 Tech Stack
*   **Framework:** ASP.NET Core (C#)
*   **Database:** PostgreSQL
*   **ORM:** Entity Framework Core
*   **Authentication:** JWT (JSON Web Tokens)
*   **Architecture:** API-driven MVC

## ✨ Features
*   **Role-based Access Control:** Розділення користувачів на ролі `User` та `Admin`.
*   **Personal Library:** Управління статусами ігор (Planned, Playing, Completed, Dropped) та відстеження куплених ігор.
*   **Content Moderation:** Система заявок (Submissions) для пропонування нових ігор у публічний каталог із перевіркою адміністратором.

## 📂 Project Structure
```text
GameNest.API/
├── Controllers/       # HTTP обробники запитів
├── Models/            # Сутності бази даних (User, Game, UserGame, Proposal)
├── Data/              # Контекст бази даних та міграції
├── Services/          # Бізнес-логіка та робота з токенами
└── appsettings.json   # Конфігурація підключення до БД
