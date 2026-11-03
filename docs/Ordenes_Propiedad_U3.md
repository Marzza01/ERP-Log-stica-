# U3 — Lectura de órdenes por comprador

Base: `4598a9b1425a1f6315b36a116c8883b998c81261`, rama de reconciliación.
Sin staging, commit, servidor HTTP, Docker, PostgreSQL ni migraciones.

## Modelo y evidencia

- `Orden.CompradorId` es la identidad persistida del comprador. Checkout la deriva de
  `ClaimTypes.NameIdentifier`, emitido por login desde `Usuario.Id`; la política JWT
  valida firma, emisor, audiencia, expiración y GUID concordante. No se deriva de
  un correo, nombre, parámetro de consulta ni `VendedorId` aportado por el cliente.
- `Usuario.VendedorId` identifica al vendedor comercial, distinto de `Usuario.Id`.
  Login lo incluye para rol Vendedor, pero un claim firmado no prueba qué artículos
  pertenecen al vendedor dentro de una orden concreta.
- `Orden` tiene un solo `VendedorId`, pero contiene varios `ItemOrden.ProductoId`.
  Los items no guardan vendedor y su referencia a Catalog es cross-schema sin FK.
  CrearOrdenRequest permite varios productos y un vendedor de cabecera introducido
  por Admin, sin demostrar correspondencia de todos los productos. Checkout usa
  el vendedor del producto comprado, pero eso no acredita las órdenes administrativas
  ni históricas. No se supone que la cabecera otorgue propiedad de toda la orden.
- FinanceDbContext trata CompradorId/VendedorId como GUID requeridos e indexados,
  sin una FK de usuario que certifique retrospectivamente su significado. No se
  verificaron datos históricos ni se repararon identidades.
- OMS consulta FinanceDbContext directamente en OrdenesController. No existe un
  servicio/repositorio de órdenes utilizado por esas cinco acciones. Servicios
  de checkout, contabilidad y fulfillment intervienen en otros flujos y se conservan.

Integración y respaldo U0 contienen el mismo OrdenesController: alta Admin,
validación de actor, listados/KPIs y transiciones acotados por VendedorId del token.
Ese filtro de cabecera se rechaza como prueba suficiente de exclusividad de items.
No se copiaron numeración, cálculo monetario, reservas, despacho ni contabilidad
de integración; son divergencias funcionales ajenas a U3. Se recupera el criterio
de fallar ante identidad inválida y aplicar el ámbito en la consulta, adaptado al
comprador persistido que sí emplean checkout y seguimiento actuales.

## Políticas finales

| Método y ruta | Política | Resultado de U3 |
|---|---|---|
| GET `/api/Ordenes` | Admin | Listado global intacto; no se ofrece listado a comprador/vendedor. |
| GET `/api/Ordenes/{id:guid}` | Admin o Comprador propietario | Única ampliación de las 81 restricciones U2; quedan 80 rutas exclusivamente Admin. |
| POST `/api/Ordenes` | Admin | Alta para terceros intacta; ningún usuario ordinario puede asignar propietario. |
| PUT `/api/Ordenes/{id:guid}/estado` | Admin | Máquina de estados y efectos contables intactos. |
| GET `/api/Ordenes/kpis` | Admin | Métricas globales intactas. |

La autenticación se exige en el controlador; cada acción lleva su rol explícito.
La prueba exhaustiva verifica la combinación de atributos, no solo el del método.
Vendedor obtiene 403 por middleware en todas las acciones de órdenes, aunque conozca
el ID o tenga un VendedorId coincidente. No se habilitan acciones nuevas para él.
La modificación del estado, cancelación y despacho OMS sigue siendo administrativa;
fulfillment, devoluciones RMA y todas las demás áreas conservan exactamente U2.

## Lectura individual y contrato

Obtener comprueba autenticación e identidad GUID no vacía/concordante. Para Comprador,
combina `o.Id == id && o.CompradorId == actorId` en la consulta antes de cargar Items.
Usa AsNoTracking y no carga SplitPago. Una orden ajena, inexistente o con ID vacío
produce el mismo NotFound vacío para el comprador. No se certifica indistinguibilidad
temporal. Un usuario autenticado con otro rol se rechaza antes de consultar datos.

Admin conserva su consulta y objeto Orden con Items/SplitPago; no se cambia su JSON,
direcciones, importes ni efectos funcionales. El caso 404 administrativo conserva su
mensaje existente. La validación JWT ya exigía identidad válida a Admin; la acción
también la comprueba cuando se invoca directamente.

Comprador recibe un contrato nuevo para ese rol, OrdenCompradorResponse: cabecera de
su compra, importes, comprobante, envío y líneas compradas. Excluye SplitPago,
TransaccionId, porcentajes/importes de comisión y vendedor, liquidaciones,
IdempotencyKey, CheckoutPayloadHash, identidades de cabecera y navegaciones cíclicas.
No devuelve la entidad EF sin filtrar. El comprador puede ver las líneas de su compra
aunque su vendedor único no sea demostrable; no se exponen datos financieros de
terceros. No se cambia el contrato administrativo ni los DTO de checkout/pago.

Checkout POST ya deriva comprador del token y acota idempotencia por ese comprador.
Seguimiento ya compara orden.CompradorId con NameIdentifier, devolviendo NotFound
para orden ajena. Esos archivos permanecen idénticos a HEAD. No se modifican reglas
de cobro, reservas, liberación/consumo, creación de orden ni transiciones.

## Pruebas locales

- API compilada con `--no-restore`, cero errores; nueve advertencias preexistentes
  CS0618 en Catalog sobre HasCheckConstraint. No se restauraron/instalaron paquetes.
- 29 casos de autorización U2 siguen pasando. Único ajuste de expectativas: permitir
  Comprador en Obtener. Se recorren 23 controladores, 83 acciones y 84 rutas, con
  anónimo, Vendedor, Comprador, Admin y JWT inválido/expirado. Listados y escrituras
  globales permanecen vedados a usuarios ordinarios.
- Nueve casos nuevos ejecutan únicamente Obtener con EF InMemory 8.0.8 o sin proveedor
  para probar rechazo previo a datos: comprador A frente a B; IDs de orden ajenos,
  inexistentes y vacíos; identidad manipulada mediante query y claims alternativos;
  respuesta mínima sin datos de pago; orden propia con varios items; respuesta Admin
  original; rechazo de Vendedor/rol desconocido; anónimo; identidad vacía/contradictoria.
- Resultado: 38 casos aislados aprobados, cero fallidos. Misma fuente Fact/Theory,
  compilada por Roslyn y llamada por runner local con dependencias ya en caché.
  El compilador manual avisa CS1701 por referencias xUnit reutilizadas.
- EF InMemory crea solo datos sintéticos en memoria del proceso. Se usa SaveChanges
  sobre ese proveedor; no se llama EnsureCreated, EnsureDeleted ni Migrate. No se
  configura Npgsql, no se ejecutan servicios de pago/stock ni el entry point real.

No es una ejecución de la suite xUnit completa ni HTTP de extremo a extremo.
El middleware prueba metadatos; las pruebas de acción invocan Obtener directamente.
InMemory no certifica traducción SQL Npgsql, integridad relacional, aislamiento ni
concurrencia PostgreSQL. La suite Finance completa sigue sin ejecutarse por los assets
de dependencias previos; no se autoriza restore. No se afirma cobertura funcional de
pagos, checkout o de las escrituras administrativas porque no se ejecutaron.

## Límites y requisitos posteriores

Para devolver acceso a Vendedor se requiere resolver pertenencia de cada producto y
datos históricos, diseñar una respuesta por vendedor sin exponer otras líneas, y
definir permisos de transición que no modifiquen recursos ajenos. Una simple
comparación con Orden.VendedorId o un ID de cliente no basta. Se mantiene Admin.

Quedan pendientes validación controlada con PostgreSQL, revisión de propiedad de datos
históricos, revocación de tokens al cambiar roles/desactivar usuarios y arranque HTTP
sin migraciones (U4). Los contratos de lectura compradora deben revisarse antes de
consumo frontend; no se modificó el frontend. El listado comprador no se ha creado.
No se autoriza producción, merge, migración ni ejecución de servicios.

## Archivos U3

- `src/MarketplaceERP.API/Controllers/OrdenesController.cs`
- `src/MarketplaceERP.API/Contracts/OrdenCompradorResponse.cs`
- `tests/MarketplaceERP.Finance.Tests/AutorizacionU2Tests.cs`
- `tests/MarketplaceERP.Finance.Tests/OrdenesPropiedadU3Tests.cs`
- `docs/Autenticacion_Autorizacion_U2.md`
- `docs/Ordenes_Propiedad_U3.md`
