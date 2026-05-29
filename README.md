# 📦 BLZipper

A packaging utility for BioLogic device data used in the Chemotion ELN converter and its shuttle service tool.

## ⚠️ Disclaimer

This project is developed and maintained independently and is **not affiliated with, sponsored by, or endorsed by BioLogic**.
BioLogic and any related product names or trademarks are the property of their respective owners and are used here **for identification and compatibility purposes only**.

This software does **not** include, contain, reimplement, or rely on any proprietary code, SDKs, or software components from BioLogic.
It only reorganizes and packages data files based on experimental grouping.
The software is provided *as-is*, without any express or implied warranty.

Use this tool at your own risk.
I do **not** provide official BioLogic support, troubleshooting, or customer service.

---

# 📘 User guide (Program version v3)

## ⭐ Overview

BLZipper packages BioLogic-measurement data into structured `tar.gz` archives.
It supports multiple BioLogic export layouts, including ZIP archives, TAR archives, and raw folder input.

The output structure is designed for Chemotion ELN usage, grouping files based on measurement identity and method characteristics.

---

## 🧩 Core features

### ✔️ Accepts multiple input formats

BLZipper can process:

| Input type  | Handling                           |
| ----------- | ---------------------------------- |
| `.zip` file | Extracted to a temporary directory |
| `.tar` file | Extracted to a temporary directory |
| Folder path | Used directly without extraction   |

> If input is an archive, BLZipper extracts it into an isolated temp folder that is deleted after processing.

---

### ✔️ Smart grouping system

Files are grouped by base filename, without extension.
Each group generates its own TAR archive:

```
part_<name>_<counter>.tar.gz
```

If a group does **not** contain `.mpr` files, the output name marks this:

```
part_<name>_<counter>_NO_MPR_found.tar.gz
```

---

### ✔️ MPS matching system

If multiple `.mps` files exist, the tool automatically selects the most relevant one based on filename similarity scoring.

Scoring rules:

* Sequential character match = +1
* First 10 matching characters = double weight
* Best score wins
* Tie breaker = shortest filename

---

### ✔️ Owner tagging

If filenames begin with:

```
OWNER_...
```

and OWNER contains no spaces, the output filename is tagged:

```
OWNER_part_<...>
```

---

### ✔️ MPS-only collection

All `.mps` files are also packed separately into dedicated archives:

```
part_<mpsName>_mps_only.tar.gz
```

---

### ✔️ Folder cleanup

If the input came from an archive, the temporary working folder is automatically deleted.
No original data is ever modified or overwritten.

---

## 🚀 Usage

### 1️⃣ Graphical interface

Run the `.exe` without arguments:
→ A file dialog opens.
→ Select a `.zip` or `.tar` file.
→ Output files will be created in the same directory.

---

### 2️⃣ Command line usage

#### ✔ Input: ZIP or TAR archive

```
BioLogicZipper.exe path\to\data.zip
```

#### ✔ Input: folder path (no archive needed)

```
BioLogicZipper.exe path\to\folder
```

#### ✔ Input and custom output folder

```
BioLogicZipper.exe path\to\data.zip path\to\output\
```

---

## 📁 Output format example

Input folder containing:

```
A01.mps
A01.0.mpr
A01.0.sqlite
A01.1.mpr
A01.1.sqlite
```

Example output:

```
part_A01_1.tar.gz
part_A01_2.tar.gz
part_A01_mps_only.tar.gz
```

---

## 🔐 Data safety

* No in-place file modification
* All packing is performed on copied files
* No original content is deleted
* Temp folders are cleaned after use
* No network activity

---

## 🛠 System requirements

* Windows 10 or later
* .NET 8 runtime (self-contained builds need no runtime installed)

---

## 🏗 Build and publish

Install the Windows .NET SDK to build this WinForms project. Native Linux/WSL `dotnet` can fail because `Microsoft.NET.Sdk.WindowsDesktop` is not part of the Linux SDK. From WSL, call the Windows-native `dotnet.exe` instead:

```
"/mnt/c/Program Files/dotnet/dotnet.exe" restore BioLogicZipper.sln
"/mnt/c/Program Files/dotnet/dotnet.exe" build BioLogicZipper.sln
"/mnt/c/Program Files/dotnet/dotnet.exe" publish BioLogicZipper.csproj -c Release
```

Release publishing is configured in `BioLogicZipper.csproj` for a self-contained `win-x64` single-file executable in `dist\`. The published `.exe` is a Windows application and should be tested on Windows.

---

## 📄 License

MIT License

---

