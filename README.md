# Montarias

Mod de montarias do Deadheim para o **Valheim 1.0** (BepInEx + ServerSync, sem Jotunn).

Por enquanto só existe a **Capivara**, que é a base para as próximas montarias (porco,
Eikthyr, veado...).

## Habilidade e montarias (tipo WoW)

Para invocar uma montaria é preciso:

1. a **Habilidade de Montaria** no nível que ela exige (`HabilidadeMinima`), e
2. **possuir** a montaria.

As duas coisas são vendidas pelo NPC **Mestre das Montarias** do NpcValheim (abas
*Treinamento* e *Montarias*), cada item na moeda que o servidor configurar (Deadcoins ou
Coins). Os níveis acima do exigido deixam todas as suas montarias mais rápidas.

Os níveis ficam em `[Habilidade] Niveis` (`nome:velocidade|...`). O padrão é
`Montaria Aprendiz:1|Montaria Experiente:1.25|Montaria Mestre:1.5`.

## Como usar

- **U** abre o menu de montarias, de qualquer lugar, na mesma janela dos NPCs:
  - *Montarias*: todas as montarias, o que falta para cada uma, **Montar** e **Usar no H**;
  - *Habilidade*: o seu nível e os próximos;
  - *Admin* (só admin): stats de cada montaria e a sua própria ficha no servidor.
- **H** monta na montaria escolhida, como no WoW: termina a conjuração (cancelada por
  movimento, ataque ou pulo) e você **já está montado**; ela não aparece do lado. **H** de
  novo desmonta, e a montaria some. Desmontar sempre funciona.
- Montaria só existe com alguém em cima: se ficar vazia (o cavaleiro morreu montado, não deu
  para montar ali) ela some sozinha, e ela não fica salva no mundo; sai junto com o jogador
  quando ele desloga.
- Montado: **Espaço** salta, **clique** dá uma investida.
- Console: `javali` abre o menu.
- Em combate (buff `DH_Combat` do Deadheim) não dá para invocar nem montar.

## Posse (no servidor)

Nível e montarias de cada personagem ficam **no servidor**, em
`BepInEx/config/ValheimMontarias/cavaleiros/<personagem>-<conta>.txt` (`rank=N` e
`mounts=id,id`), a mesma chave dos saldos de Deadcoins. Só o servidor lê e grava, e ele
descobre de quem é a ficha pela própria conexão; o cliente só recebe a própria ficha.
Cada mudança fica em `BepInEx/config/ValheimMontarias/cavaleiros.log`.

- Admin: aba *Admin* → **Dar esta montaria a mim**, **Aprender o próximo nível**,
  **Zerar minha habilidade e montarias** (o servidor confere a adminlist).
- `LiberarTodasGratis = true` dá o nível máximo e todas as montarias a todo mundo.

### API para outros mods

`ValheimMontarias.MontariasApi` (estática, só tipos primitivos) é o que o NpcValheim chama
por reflexão: catálogo (montarias, níveis), o estado do jogador local e, no servidor,
`ServerRank` / `ServerOwns` / `ServerGrantRank` / `ServerGrantMount` pelo remetente do RPC.

## Configuração

`BepInEx/config/com.valheimmontarias.mod.cfg`, sincronizado pelo servidor
(`LockConfig = true`):

- `[Montaria]` (Capivara): `Nome`, `VelocidadeCorrida`, `VelocidadeAndar`, `AlturaPulo`,
  `Escala`, `CastSegundos`, `Vida`, `Stamina`, `DrenoStamina`, `HabilidadeMinima`.
- `[Habilidade]`: `Niveis`.
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
3. Um `MountProfile` em `MountSettings.Init` (id, nome, ícone, valores padrão, habilidade
   mínima) e incluí-lo em `MountSettings.All`. Para vender, uma entrada
   `mount=<id>;price=...;currency=...` no `[MountTrainer] Offers` do NpcValheim.
4. Encaminhar `IsOurs` / `ApplyAll` / `FitSeat` em `Prefabs/MountHub.cs`.
