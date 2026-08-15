<p align="center">
  <img width="65%" alt="fluentflyout-title" src="https://github.com/user-attachments/assets/daa2969f-8ad2-4832-8253-26133a50c921">
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/Platform-Windows%2011%20%7C%2010-0078D4?style=flat-square&logo=windows">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet">
  <img alt="License" src="https://img.shields.io/badge/License-GPL--3.0-blue?style=flat-square">
  <img alt="Status" src="https://img.shields.io/badge/Fork-Enhanced%20Edition-success?style=flat-square">
</p>

---

**FluentFlyout (Enhanced Edition)** est un fork optimisé et personnalisé de [FluentFlyout](https://github.com/unchihugo/FluentFlyout), l'application moderne de flyouts et widgets multimédias conçue selon les principes de design Fluent 2 pour Windows 11.

Cette édition personnalisée apporte des améliorations majeures en matière de **performances temps réel**, de **support multi-artistes**, d'**esthétique dynamique** et d'**architecture de code modulaire**.

---

## 🚀 Fonctionnalités & Améliorations de cette version

### ⚡ Réactivité instantanée (< 10 ms)
- **Élimination de la latence de ~1s** : Optimisation du cycle de vie des flux Windows Media (GSMTC/COM) en évitant les requêtes IPC redondantes et les ouvertures de flux multiples.
- **Hachage ultra-rapide (FNV-1a)** : Calcul du hachage de la pochette directement en mémoire en quelques microsecondes (au lieu du SHA-256 lourd).
- **Mise à jour immédiate du texte** : Affichage instantané du titre et de l'artiste dès l'événement de changement de musique, avec chargement asynchrone et fluide de la pochette.

### 👥 Support Multi-Artistes complet
- **Extraction exhaustive des artistes** : Détection et fusion automatique de tous les artistes participants depuis les métadonnées `Artist`, `AlbumArtist` et `Subtitle` (ex: *Werenoi, Damso, Ninho* au lieu d'afficher uniquement le premier).
- **Normalisation intelligente** : Nettoyage et séparation propre par virgules sans doublons.
- Intégré partout : Widget barre des tâches, flyout principal et pop-up "À suivre" (*Next Up*).

### 🎨 Design dynamique & Couleurs d'accent
- **Dégradé adaptatif** : L'arrière-plan du flyout extrait en temps réel les deux couleurs dominantes de la pochette d'album pour générer un dégradé fluide.
- **Contrôles harmonisés** : Le bouton Play/Pause adopte automatiquement la couleur d'accent de l'album en cours de lecture.
- **Lisibilité optimisée** : Contraste soigné en mode clair et mode sombre.

### 📜 Widget de la Barre des Tâches & Marquee Text
- **Défilement dynamique (Marquee)** : Défilement fluide automatique pour les titres longs avec masquage précis (*clipping*), et affichage fixe et centré pour les titres courts.
- **Redimensionnement adaptatif** : Ajustement automatique de la largeur (294px avec boutons de contrôle, 192px en mode compact).

### 🏗️ Architecture C# Modulaire
- Refactoring du code principal en classes partielles spécialisées (`MainWindow.Animation.cs`, `MainWindow.MediaHandlers.cs`, `MainWindow.UpdateUI.cs`, `MainWindow.Seekbar.cs`, `MainWindow.WndProc.cs`, etc.).
- Gestion robuste du mode instance unique (*Single Instance*) via Mutex Windows sécurisé.

---

## 📸 Aperçu

<div align="center">
  <img height="380px" alt="Taskbar Widget" src="https://github.com/user-attachments/assets/43963c54-e2d8-4b93-9842-482e12b2c592" />
</div>

<div align="center">
  <img height="180px" src="https://github.com/user-attachments/assets/4dab1c12-594a-4785-bddc-0da1783bf1c8"> 
  <img height="180px" src="https://github.com/user-attachments/assets/b4306026-b274-418b-a39e-78877e7610a7"> 
  <img height="180px" src="https://github.com/user-attachments/assets/39de69fe-54c8-4b22-880c-7f0370b8dd9c">
</div>

---

## 🛠️ Compilation et Exécution

### Prérequis
- Windows 10 (22000+) ou Windows 11
- [.NET SDK 10.0](https://dotnet.microsoft.com/download)

### Compiler le projet
```powershell
dotnet build "FluentFlyoutWPF\FluentFlyout.csproj" -c Release -p:Platform=x64
```

### Lancer l'application
L'exécutable autonome est disponible après la compilation dans :
```
FluentFlyoutWPF\bin\x64\Release\net10.0-windows10.0.22000.0\FluentFlyout.exe
```

---

## 📜 Licence & Remerciements

Ce projet est un fork dérivé du projet open-source **[FluentFlyout](https://github.com/unchihugo/FluentFlyout)** créé par **[Hugo Li (unchihugo)](https://github.com/unchihugo)**.

Conformément à la licence d'origine, ce logiciel est distribué sous les termes de la licence **GNU General Public License v3.0 (GPL-3.0)**. Consultez le fichier [LICENSE](LICENSE) pour plus de détails.

### Dépendances et bibliothèques open-source :
- [Dubya.WindowsMediaController](https://github.com/DubyaDude/WindowsMediaController)
- [MicaWPF](https://github.com/Simnico99/MicaWPF)
- [WPF-UI](https://github.com/lepoco/wpfui)
- [Microsoft.Toolkit.Uwp.Notifications](https://github.com/CommunityToolkit/WindowsCommunityToolkit)
- [NAudio](https://github.com/naudio/NAudio)
- [NLog](https://nlog-project.org/)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
