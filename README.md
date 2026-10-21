# Montarias

Mod de montarias do Deadheim para o **Valheim 1.0** (BepInEx + ServerSync, sem Jotunn).

Montarias: **Capivara**, **Neck**, **Deer**, **Boar**, **Eikthyr** e **Moder**, que voa.

## Habilidade e montarias (tipo WoW)

Para invocar uma montaria é preciso:

1. a **Habilidade de Montaria** no nível que ela exige (`HabilidadeMinima`),
2. **possuir** a montaria, e
3. se ela pedir (`PasseExigido`), o **passe do chefe** do NpcValheim (BossPass: ganho ao
   ajudar a matar o chefe ou comprado no Mestre dos Passes). O Moder pede o passe do Moder
   (`Dragon`). Sem o NpcValheim vale ter ajudado a matar o chefe (registro do perfil).

As duas coisas são vendidas pelo NPC **Mestre das Montarias** do NpcValheim (abas
*Treinamento* e *Montarias*), cada item na moeda que o servidor configurar (Deadcoins ou
Coins). Os níveis acima do exigido deixam todas as suas montarias mais rápidas e encurtam
a conjuração.

Os níveis ficam em `[Habilidade] Niveis` (`nome:velocidade:cast|...`; o `cast` multiplica o
tempo de conjuração e é opcional). O padrão é
`Montaria Aprendiz:1:1|Montaria Experiente:1.25:0.75|Montaria Mestre:1.5:0.5|Montaria Voadora:1.5:0.5`.
O nível 4, **Montaria Voadora**, é o que deixa voar: as montarias que voam exigem ele por
padrão. Uma config ainda num padrão antigo (sem cast, ou só com 3 níveis) é atualizada
sozinha; uma editada fica como está.

## Como usar

Como no WoW, **não existe montaria vazia**:

- **Use o item da montaria** (o *Apito da Capivara*, na hotbar ou no inventário): termina a
  conjuração (cancelada por movimento, ataque ou pulo) e você **aparece montado**.
  Não dá para invocar no leme do barco nem sentado/deitado em algo.
- **Saiu, sumiu:** usar o item de novo ou **H** tiram você da montaria, e ela some na
  hora. Se o cavaleiro morre montado, ela some também. Se não der para montar ali, ela nem
  chega a aparecer. Ela não fica salva no mundo e sai junto com o jogador no logout.
- O item vem para a bolsa quando você ganha a montaria (compra no Mestre das Montarias ou
  admin). Perdeu? No menu, **Pegar o item**. Ele só funciona para quem possui a montaria no
  servidor: uma cópia não serve para mais ninguém.
- **H** faz o mesmo que o item, para a montaria escolhida no menu.
- **U** abre o menu de montarias, de qualquer lugar, na mesma janela dos NPCs:
  - *Montarias*: todas as montarias, o que falta para cada uma, **Montar**, **Usar no H** e
    **Pegar o item**;
  - *Habilidade*: o seu nível e os próximos;
  - *Admin* (só admin): stats de cada montaria (inclusive a pose montado e os ajustes finos
    de sela, personagem e pernas, que mudam na hora mesmo já montado) e a sua própria ficha
    no servidor.
- Montado: **Espaço** salta, **clique** dá uma investida.
- **Moder (voa):** **Espaço** decola e, no ar, sobe; **Ctrl** desce e, perto do chão
  firme, pousa; para frente ela vai para onde a câmera olha (subindo ou descendo junto);
  sem tecla ela paira. Não passa de `AlturaMaximaVoo` acima do chão ou da água. Montado não
  se toma dano de queda (o assento sobe e desce com o corpo); descer ou ser derrubado no ar
  é uma queda normal, com o dano de queda do jogo.
- **Qualquer golpe derruba:** um golpe no cavaleiro ou na montaria (monstro, jogador,
  armadilha, torre...) tira o cavaleiro de cima e a montaria some. Queda, afogamento, fogo,
  veneno e frio contínuos não derrubam; jogador com PvP desligado não é derrubado por
  jogador. A montaria em si nunca toma dano.
- Console: `javali` abre o menu.
- Em combate (buff `DH_Combat` do Deadheim) não dá para montar; descer sempre dá.

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

`ValheimMontarias.MontariasApi` (estática, só tipos primitivos, `Version = 2`) é o que o
NpcValheim chama por reflexão: catálogo (montarias, níveis), o estado do jogador local e, no
servidor, `ServerRank` / `ServerOwns` / `ServerGrantRank` / `ServerGrantMount` pelo remetente
do RPC.

Desde a versão 2, para o **estábulo** do Deadheim (Aesir 20 — guardar/chamar a montaria
registrada a partir de um ponto fixo): `LocalIsRiding`, `LocalRiddenMountId` (o que guardar),
`LocalSummon(mountId)` e `LocalDismount()` (chamar/guardar de volta). São só atalhos para o
mesmo invocar/desmontar do item/H — a posse, a habilidade e o passe de chefe continuam sendo
conferidos do jeito de sempre, então o estábulo não dá nada que o jogador já não tivesse.

## Cosméticos do jogador (Aesir 28 e 30, VIP)

Quem **possui** uma montaria (`RiderServer`) pode, no menu *Montarias* (U), com ela
selecionada:

- **Nome** (Aesir 28): um nome visível para todo mundo acima da montaria, em vez do nome
  padrão da config. Guardado por personagem e por montaria na própria ficha do servidor
  (`cavaleiros/<personagem>-<conta>.txt`, linhas `name_<id>=...`).
- **Skin** (Aesir 30): um tingimento cosmético (mesmos stats) escolhido entre as opções que o
  admin configurou para aquela montaria (`[Montaria<id>] Skins`). Guardado do mesmo jeito
  (`skin_<id>=...`).

Os dois são exclusivos de VIP por padrão (`NomeMontariaSoVip` / `SkinMontariaSoVip`,
`[Cosmeticos]`) e podem ser desligados inteiramente (`NomeMontariaAtivo` /
`SkinMontariaAtivo`). O servidor confere posse e VIP a cada pedido
(`RiderNet.ActionSetName/ActionSetSkin`); a integração com `VipList.dll` é por reflexão
(`Integration/VipProvider.cs`, mesmo padrão do `GuildsClanProvider`/`VipProvider` do Velas),
então sem o VipList instalado ninguém passa a checagem de VIP.

**Como aparece para os outros jogadores:** o nome/skin escolhidos ficam gravados no ZDO da
montaria no momento em que ela é invocada (o próprio dono grava, já aprovado pelo servidor),
e todo cliente que a vê lê o mesmo ZDO — por isso aparecem iguais para todo mundo, sem RPC
novo por invocação. **Limite conhecido:** como o resto da economia deste mod (gold pago no
cliente no Mestre das Montarias, por exemplo), um cliente modificado poderia escrever outro
valor nesse campo e mostrar um nome/skin não aprovado aos outros — aceitável aqui porque é
puramente cosmético (sem vantagem de combate) e seguindo o mesmo nível de confiança que o
resto do mod já usa.

A skin (tingimento) é aplicada por `MaterialPropertyBlock` em cima do material, sem alterar o
asset compartilhado; no modelo próprio da Capivara (`.skin`/`.png`) o resultado é certo, nas
montarias que ainda usam a malha vanilla (Neck, Deer, Boar, Eikthyr, Moder) o efeito depende
de o shader da criatura aceitar `_Color`/`_BaseColor`/`_TintColor` — testar no jogo.

## Configuração

`BepInEx/config/com.valheimmontarias.mod.cfg`, sincronizado pelo servidor
(`LockConfig = true`):

- `[Montaria<id>]`: `Nome`, `VelocidadeCorrida`, `VelocidadeAndar`, `AlturaPulo`,
  `Escala`, `PrefabSela`, `SelaRotacaoX/Y/Z` (graus; `Y` gira a sela sobre as costas),
  `PoseMontado` (`Lox`: ereto, pernas bem abertas; `Asksvin`: inclinado para a frente,
  pernas mais fechadas -- as poses de montar das selas desses bichos do jogo, lidas delas;
  `Cadeira`: a pose sentada antiga, com as mãos nos joelhos; ou o nome de uma animação
  `attach_` do jogador), os ajustes finos em metros `AjusteSelaAltura`, `AjusteSelaFrente`,
  `AjustePersonagemAltura`, `AjustePersonagemFrente` e `FolgaPernas` (veja *Encaixe*), `CastSegundos`,
  `HabilidadeMinima`, `PasseExigido`, `Skins` (Aesir 30: `Nome:#RRGGBB` separados por `|`;
  "Padrao" sem tingimento sempre existe); nas que voam, `VelocidadeVoo` e
  `VelocidadeVooCorrida`.
- `[Habilidade]`: `Niveis`.
- `[Voo]`: `AlturaMaximaVoo` (metros acima do chão ou da água), `VelocidadeSubida`.
- `[Geral]`: `LockConfig`, `LiberarTodasGratis`, `DerrubarAoTomarHit`, `TeclaMenu`, `TeclaInvocar`.
- `[Cosmeticos]`: `NomeMontariaAtivo`, `NomeMontariaSoVip`, `NomeMontariaTamanho` (Aesir 28);
  `SkinMontariaAtivo`, `SkinMontariaSoVip` (Aesir 30).

Servidor e clientes precisam estar na mesma versão do mod.

### Encaixe de sela, personagem e pernas

Tudo se encaixa sozinho em cada montaria, na pose em que ela está (`MountGear.SitOnPosedBack`
e `RiderVisualFix`), alguns quadros depois de ela aparecer e de novo ao decolar ou pousar:

- **Sela:** sobre as costas, no meio do corpo (acima da raiz da montaria). A altura é a
  primeira superfície acima do osso do tronco mais próximo (`Spine`, `Pelvis`...): asa
  dobrada por cima, galhada e ponta de espinho ficam de fora.
- **Personagem:** o quadril vai no meio do assento (o fundo da sela, entre a frente e a
  traseira), descontando onde cada pose põe o quadril (a do Lox, meio metro atrás da raiz).
  O cliente de quem monta confere isso no próprio esqueleto e corrige se for diferente.
- **Pernas:** as coxas giram até os joelhos ficarem a `FolgaPernas` dos flancos, e a canela
  mantém o ângulo da pose. Vale também para os outros jogadores montados que você vê.

Os ajustes finos (`AjusteSela*`, `AjustePersonagem*`, `FolgaPernas`) só empurram esse
resultado. As chaves antigas `AlturaAssento`, `AssentoOffsetX/Z`, `SelaOffsetX/Y/Z` e
`AberturaPernas` eram compensações da lógica anterior e não são mais lidas (podem ser
apagadas do cfg). Cada montada deixa no log uma linha `rider on ...` com a altura do quadril
sobre o assento e a distância dos joelhos aos flancos.

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

1. Um `Prefabs/<Nome>Prefab.cs` no molde do `MountPrefab` (clona o bicho vanilla, põe a
   sela e um `MountControl`).
2. Registrar e finalizar o prefab em `Patches/ZNetScenePatch.cs`.
3. Um `MountProfile` em `MountSettings.Init` (id, nome, ícone, valores padrão, habilidade
   mínima) e incluí-lo em `MountSettings.All`. Para vender, uma entrada
   `mount=<id>;price=...;currency=...` no `[MountTrainer] Offers` do NpcValheim.
4. Encaminhar `IsOurs` / `ApplyAll` / `FitSeat` em `Prefabs/MountHub.cs`.
