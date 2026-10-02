# Montarias

Mod de montarias do Deadheim para o **Valheim 1.0** (BepInEx + ServerSync, sem Jotunn).

Por enquanto só existe a **Capivara**, que é a base para as próximas montarias (porco,
Eikthyr, veado...). Ela é exclusiva: só invoca quem tem a montaria liberada.

## Como usar

- **U** abre o menu de montarias (aba *Minhas Montarias* e, para admin, aba *Admin*).
- **H** invoca ou recolhe a montaria selecionada (com tempo de conjuração, cancelado por
  movimento, ataque ou pulo).
- Montado: **Espaço** salta, **clique** dá uma investida.
- **E** na montaria: admin abre os ajustes; **Shift+E** alterna seguir/ficar.
- Console: `javali` abre o menu.
- Em combate (buff `DH_Combat` do Deadheim) não dá para invocar nem montar.

## Posse

A posse fica em `Player.m_customData["vm_owned"]`, salva no arquivo do personagem.
Posses antigas que estavam no ZDO do jogador (perdidas a cada relog) são migradas ao
serem lidas.

- Admin: aba *Admin* → **Dar esta montaria a mim**.
- `LiberarTodasGratis = true` libera todas as montarias para todo mundo.

## Configuração

`BepInEx/config/com.valheimmontarias.mod.cfg`, sincronizado pelo servidor
(`LockConfig = true`):

- `[Montaria]` (Capivara): `Nome`, `VelocidadeCorrida`, `VelocidadeAndar`, `AlturaPulo`,
  `Escala`, `CastSegundos`, `Vida`, `Stamina`, `DrenoStamina`.
- `[Geral]`: `LockConfig`, `LiberarTodasGratis`, `TeclaMenu`, `TeclaInvocar`.

Servidor e clientes precisam estar na mesma versão do mod.

## Compilar

O build usa o mesmo `Directory.Build.props` dos outros mods do Deadheim: compila contra a
árvore de referência `D:\valheim-ref` (Valheim 1.0.16), ou contra `VALHEIM_PATH` se a
variável existir. O alvo `ValidateValheimReferences` recusa o build se os
`publicized_assemblies` forem mais velhos que as DLLs do jogo. Depois de um patch do jogo,
rode `deadheim-deploy\bin\new-valheim-ref.ps1`.

```
dotnet build -c Release
```

O build copia a DLL e os assets para `$(ValheimPath)\BepInEx\plugins\ValheimMontarias`.

## Adicionar uma montaria nova

1. Um `Prefabs/<Nome>Prefab.cs` no molde do `BoarPrefab` (clona o bicho vanilla, põe a
   sela e um `JavaliControl`).
2. Registrar e finalizar o prefab em `Patches/ZNetScenePatch.cs`.
3. Um `MountProfile` em `MountSettings.Init` (id, nome, ícone, valores padrão) e incluí-lo
   em `MountSettings.All`.
4. Encaminhar `IsOurs` / `ApplyAll` / `FitSeat` em `Prefabs/MountHub.cs`.
