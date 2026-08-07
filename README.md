# FirebirdMetadataGenerator



Proste narzędzie CLI do zarządzania strukturą baz danych  * *Firebird 5.0 * *.



## Funkcjonalności



* eksport struktury bazy do skryptów SQL,

* tworzenie nowej bazy ze skryptów,

* aktualizacja istniejącej bazy.



Obsługiwane elementy:



* domeny,

 * tabele i kolumny,

 * procedury PSQL.



 ## Wymagania



 * .NET 8

 * Firebird 5.0



 ## Użycie



 ### Budowanie nowej bazy



```powershell

dotnet run -- build-db --db-dir "C:\DB" --scripts-dir "C:\Scripts"

```



 ### Eksport skryptów z bazy



```powershell

dotnet run -- export-scripts --connection-string "..." --output-dir "C:\Scripts"

```



 ### Aktualizacja bazy



```powershell

dotnet run -- update-db --connection-string "..." --scripts-dir "C:\Scripts"

```



 ## Struktura skryptów



```text

Scripts

│
├── 01 _domains.sql
├── 02 _tables.sql
└── 03 _procedures.sql

```

Kolejność wykonywania:

1. Domeny
2. Tabele
3. Procedury

 ## Aktualizacja różnicowa

Narzędzie umożliwia:


 * dodać brakujące domeny,

 * dodać brakujące kolumny,

 * zaktualizować procedury przez `CREATE OR ALTER`.





