# Preparar Wompi (sandbox) y Firebase (Firestore) para SoccerBet

Guia para dejar listas las cuentas y credenciales ANTES de pedirle a Claude Code el RF12 (recarga de saldo).
Todo es gratis y sin dinero real. Tiempo estimado: 30 a 45 minutos.

> Nota de verificacion: los datos de prueba de Wompi y la formula de la firma de integridad se confirmaron en la documentacion
> oficial de Wompi. Los nombres exactos de menus y botones (de Wompi y de la consola de Firebase) pueden cambiar; si algo no
> coincide, busca la opcion equivalente. Claude Code debe leer la documentacion oficial antes de integrar (regla 9 de CLAUDE.md).

---

## Parte A. Wompi (pasarela de pagos, modo de pruebas)

### A1. Crear la cuenta
1. Entra a https://comercios.wompi.co y crea una cuenta de comercio (correo, contrasena y datos basicos).
2. Es posible que Wompi pida datos del negocio o documentos para **activar pagos reales**. Para este proyecto NO los necesitas:
   solo vas a usar el **ambiente de pruebas (sandbox)**. Si en el registro te exige documentos antes de mostrarte las llaves de
   prueba, escribe a soporte de Wompi o dime que ves y lo resolvemos.

### A2. Encontrar las llaves de pruebas
1. Dentro del panel, busca la seccion de desarrolladores (algo como **Desarrolladores** o **Developers**) y el apartado de
   **llaves / secretos para integracion tecnica**.
2. Asegurate de estar en el modo **Pruebas (Sandbox)** y no en Produccion. Las llaves de pruebas se reconocen por su prefijo:

| Llave | Prefijo | Para que sirve | Donde puede usarse |
|---|---|---|---|
| Publica | `pub_test_` | Identifica tu comercio al abrir el checkout | Navegador (no es secreta) |
| Privada | `prv_test_` | Llamar a la API de Wompi desde tu servidor (por ejemplo consultar una transaccion) | **Solo servidor** |
| Eventos | `test_events_` | Verificar que un evento (webhook) viene realmente de Wompi | **Solo servidor** |
| Integridad | `test_integrity_` | Firmar el monto y la referencia para que nadie los altere | **Solo servidor** |

3. Copia las cuatro en un bloc de notas TEMPORAL (no en el proyecto) para pasarlas a user-secrets en la Parte C. Despues borra el bloc.
4. **Nunca pegues estas llaves en el chat, en capturas de pantalla ni en archivos del repositorio.**

### A3. Datos de prueba (confirmados en la documentacion de Wompi)
Cuando abras el checkout en sandbox, usa estos datos. No se cobra dinero real.

| Medio | Resultado | Dato |
|---|---|---|
| Tarjeta | Aprobada | `4242 4242 4242 4242` |
| Tarjeta | Rechazada | `4111 1111 1111 1111` |
| Tarjeta | (cualquiera) | Fecha de vencimiento futura y CVC de 3 digitos. Otras tarjetas dan ERROR |
| Nequi | Aprobada | `3991111111` |
| Nequi | Rechazada | `3992222222` |
| PSE | Aprobada / Rechazada | En el widget se elige un banco que aprueba o uno que rechaza |

### A4. Como funciona el cobro (para entender lo que hara Claude Code)
1. Tu servidor crea una **referencia unica** por cada recarga (no se puede reutilizar) y el monto **en centavos**
   (ejemplo: $20.000 COP = `2000000`).
2. Tu servidor calcula la **firma de integridad**: se concatena `referencia + monto_en_centavos + moneda + secreto_de_integridad`
   y se le aplica SHA-256. Esto SIEMPRE se hace en el servidor, nunca en el navegador, para no exponer el secreto.
3. Se abre el Web Checkout / widget de Wompi con: llave publica, moneda (COP), monto, referencia, firma e `redirect-url`.
4. El cliente paga. Wompi lo devuelve a tu `redirect-url` con el id de la transaccion.
5. **La redireccion es solo informativa: no se confia en ella para acreditar saldo.** Se debe confirmar el estado real de la
   transaccion con Wompi (consultandola con la llave privada, o recibiendo el evento firmado) y solo si es `APPROVED` se acredita.

> La documentacion que pude leer del checkout es la de Panama, que usa la misma estructura pero otra moneda. La de Colombia usa
> COP. Pidele a Claude Code que verifique en la documentacion de Colombia: la URL base del sandbox, los nombres exactos de los
> parametros y los estados de transaccion.

### A5. El webhook (eventos) no llega a tu computador
Wompi llama a una URL publica, y `localhost` no lo es. Para el desarrollo tienes dos caminos:
- **Camino simple (recomendado al inicio):** al volver el usuario a tu sitio, el servidor consulta a Wompi el estado de la transaccion.
- **Camino completo (despues):** abrir un tunel publico (por ejemplo Dev Tunnels de Visual Studio o ngrok) y registrar esa URL en
  el panel de Wompi como URL de eventos. Cambia cada vez que reinicies el tunel, asi que es incomodo; dejalo para el final.

---

## Parte B. Firebase (Firestore: guarda el registro de los pagos)

### B1. Crear el proyecto
1. Entra a https://console.firebase.google.com con tu cuenta de Google.
2. **Crear un proyecto** (o "Agregar proyecto"). Nombre sugerido: `soccerbet`. Firebase le agrega un sufijo unico al ID (por ejemplo `soccerbet-a1b2c`).
3. Google Analytics: **desactivalo** (no lo necesitas).
4. Deja el plan **Spark** (gratuito, sin tarjeta). No cambies a Blaze.

### B2. Crear la base de datos Firestore
1. En el menu izquierdo: **Compilacion (Build) > Firestore Database > Crear base de datos**.
2. Edicion: **Standard**.
3. Ubicacion: elige una cercana, por ejemplo `southamerica-east1` (Sao Paulo) o `nam5` (EE. UU.). **No se puede cambiar despues.**
4. Reglas de seguridad: elige **modo de produccion**. Esto deja la base cerrada a cualquier acceso desde navegadores o apps, y es lo
   que queremos: solo tu servidor entra (con la cuenta de servicio de B3). No cambies las reglas.
5. No crees colecciones a mano: la coleccion `pagos` se crea sola cuando se guarde el primer documento.
6. El plan gratuito incluye 1 GiB de almacenamiento y un cupo diario de lecturas y escrituras, de sobra para el proyecto.

**NO actives Cloud Storage** (almacenamiento de archivos) en Firebase: exige el plan Blaze (tarjeta). Las imagenes van en `wwwroot/img`.

### B3. Credenciales para tu servidor (cuenta de servicio)
1. Engranaje junto a "Descripcion general del proyecto" > **Configuracion del proyecto** > pestana **Cuentas de servicio**.
2. Pulsa **Generar nueva clave privada** y confirma. Se descarga un archivo `.json`.
3. Crea una carpeta FUERA de tu proyecto, por ejemplo `C:\secretos\`, y muevelo ahi con un nombre claro:
   `C:\secretos\soccerbet-firebase.json`.
4. **Ese archivo es una llave maestra de tu proyecto.** No lo subas a Git, no lo copies dentro del proyecto, no lo envies por chat ni por correo.
   Si se filtra: en la misma pantalla, borra esa clave y genera otra.
5. Anota el **ID del proyecto** (pestana General de Configuracion del proyecto, campo "ID del proyecto"), por ejemplo `soccerbet-a1b2c`.

---

## Parte C. Guardar todo en user-secrets

Abre una terminal (PowerShell) en la carpeta del proyecto (donde esta `CasaApuestas.csproj`). Reemplaza los valores de ejemplo por los tuyos
y mantenlos entre comillas:

```powershell
dotnet user-secrets set "Wompi:PublicKey"       "pub_test_XXXXXXXX"
dotnet user-secrets set "Wompi:PrivateKey"      "prv_test_XXXXXXXX"
dotnet user-secrets set "Wompi:EventsSecret"    "test_events_XXXXXXXX"
dotnet user-secrets set "Wompi:IntegritySecret" "test_integrity_XXXXXXXX"
dotnet user-secrets set "Firebase:ProjectId"       "soccerbet-a1b2c"
dotnet user-secrets set "Firebase:CredentialsPath" "C:\secretos\soccerbet-firebase.json"
```

Comprobar (muestra los valores en pantalla, asi que no lo hagas con alguien mirando ni lo captures):

```powershell
dotnet user-secrets list
```

Tambien puedes editarlos con clic derecho sobre el proyecto > **Administrar secretos de usuario**.
En `appsettings.json` NO escribas ninguna de estas llaves.

---

## Lista de verificacion (marca todo antes de pedir el RF12)

- [X] Tengo cuenta en Wompi y veo las 4 llaves de **pruebas** (`pub_test_`, `prv_test_`, `test_events_`, `test_integrity_`).
- [X] Tengo el proyecto de Firebase en plan **Spark** y Firestore creado en modo produccion.
- [X] Descargue el JSON de la cuenta de servicio y esta en `C:\secretos\`, fuera del proyecto.
- [X] Guarde los 6 valores en user-secrets y `dotnet user-secrets list` los muestra.
- [X] `appsettings.json` no contiene llaves, y `.gitignore` excluye `.env` y credenciales.
- [X] Hice commit de lo que ya funciona antes de empezar.

## Prompt para Claude Code (RF12)

> Vamos con **RF12: recarga de saldo con Wompi (sandbox) y Firestore**. Ya tengo las llaves en user-secrets (`Wompi:PublicKey`,
> `Wompi:PrivateKey`, `Wompi:EventsSecret`, `Wompi:IntegritySecret`, `Firebase:ProjectId`, `Firebase:CredentialsPath`).
> **Antes de escribir codigo:** lee la documentacion oficial de Wompi Colombia (URL base del sandbox, parametros del Web Checkout,
> firma de integridad, estados de transaccion) y la de `Google.Cloud.Firestore` para .NET, y no inventes campos ni firmas.
> Luego dame un plan corto con los archivos que vas a crear y espera mi aprobacion. Requisitos: pagina de recarga solo para
> clientes con sesion; la firma de integridad y la referencia unica se generan en el servidor; el monto se envia en centavos;
> el documento `pagos` en Firestore se crea en PENDIENTE con `acreditado=false` (ver `docs/firestore.md`); al volver el usuario,
> confirma el estado real de la transaccion con Wompi (no confies en la redireccion); solo si es APPROVED y `acreditado=false`
> llama a `sp_AcreditarSaldo` y marca `acreditado=true`, de forma que un pago nunca se acredite dos veces. Dejo el webhook para
> una segunda etapa. Muestrame cada archivo que modifiques y explicalo.

## Como probarlo cuando este listo
1. Inicia sesion, entra a recargar y paga con la tarjeta `4242 4242 4242 4242`: el saldo debe subir y debe aparecer un documento en Firestore con `estado = APROBADO` y `acreditado = true`.
2. Paga con `4111 1111 1111 1111`: no debe cambiar el saldo y el documento debe quedar como rechazado.
3. Recarga la pagina de retorno varias veces: el saldo NO debe subir una segunda vez.
4. En SSMS, `SELECT TOP 5 * FROM Movimiento_Saldo ORDER BY fecha DESC;` debe mostrar un unico CREDITO por pago aprobado.
