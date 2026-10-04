<!-- ERP desarrollado por Marcelo Zepeda | Todos los derechos reservados © 2026 -->

# Estructura del Libro Excel: Catalogo_500_Productos.xlsx
**LOGIMKT S.A. de C.V. — Asignatura: Logística de Negocios Digitales**
**Docente: Ing. Pedro Alejandro Castillo Flores**

Conforme a las directrices de la Sección 3 y Sección 5 (Semana 1) del proyecto integrador, el archivo maestro `Catalogo_500_Productos.xlsx` contiene 3 hojas estructuradas:

---

## Hoja 1: `Catalogo 500 Productos`
Contiene los 500 SKUs activos de la empresa con los 16 campos obligatorios de datos maestros de logística digital:

| # | Campo | Tipo | Descripción | Ejemplo Real |
| :-: | :--- | :---: | :--- | :--- |
| 1 | **SKU** | Texto | Código único alfanumérico por categoría | `ELC-0001`, `ROP-0045` |
| 2 | **Nombre del Producto** | Texto | Denominación comercial del artículo | Smartphone Pro 128GB Standard |
| 3 | **Categoría** | Texto | Una de las 6 categorías oficiales del Marketplace | Electrónica y Tecnología |
| 4 | **Subcategoría** | Texto | Clasificación de segundo nivel para catálogo y filtros | Smartphones y Accesorios |
| 5 | **Precio B2C ($)** | Decimal | Precio para consumidor final (mínimo de compra $5) | $394.51 |
| 6 | **Precio B2B ($)** | Decimal | Precio especial mayoreo/distribuidores (mínimo $500) | $307.72 |
| 7 | **Stock Inicial** | Entero | Cantidad de unidades físicas iniciales en bodega | 214 |
| 8 | **Stock Mínimo** | Entero | Umbral para punto de reorden y alerta de inventario | 44 |
| 9 | **Proveedor** | Texto | Razón social del proveedor de aprovisionamiento | Telecomunicaciones y Tecnología de C.A. |
| 10 | **País Origen** | Texto | País de fabricación o importación | China |
| 11 | **Peso (kg)** | Decimal | Peso unitario para cálculo de flete y despacho | 8.31 kg |
| 12 | **Dimensiones (cm)** | Texto | Largo x Ancho x Alto para cubicaje en almacén WMS | 56x32x29 cm |
| 13 | **Unidad de Medida** | Texto | Presentación (Unidad, Caja x12, Pack x3, etc.) | Unidad |
| 14 | **Estado** | Texto | `Activo` (disponible para venta) o `Inactivo` | Activo |
| 15 | **Código EAN** | Texto | Código de barras EAN-13 para escaneo óptico | 7410000000001 |
| 16 | **Descripción Corta** | Texto | Resumen del producto para ficha técnica en tienda | Ficha descriptiva para mercado salvadoreño |

---

## Hoja 2: `Categorías y Reglas`
Define el árbol de categorías oficiales y sus parámetros operativos para la plataforma LOGIMKT:

| Categoría Oficial | SKUs Asignados | Tipo de Cliente | Comisión Base (%) | Lead Time Promedio Proveedor |
| :--- | :---: | :---: | :---: | :---: |
| **Electrónica y Tecnología** | 100 | B2C / B2B | 10% | 15 a 30 días |
| **Ropa y Accesorios** | 90 | B2C / B2B | 12% | 7 a 14 días |
| **Alimentos y Bebidas** | 80 | B2C / B2B | 8% | 2 a 5 días |
| **Ferretería e Industria** | 80 | B2B / B2C | 10% | 5 a 10 días |
| **Hogar y Decoración** | 80 | B2C / B2B | 10% | 7 a 15 días |
| **Salud y Cuidado Personal** | 70 | B2C / B2B | 8% | 3 a 7 días |
| **TOTAL** | **500 SKUs** | **Multi-canal** | — | — |

---

## Hoja 3: `Proveedores y Parámetros`
Maestro de proveedores locales e internacionales que abastecen la operación en El Salvador:

| Proveedor | País | Tipo de Suministro | Términos de Pago (B2B) | Desempeño Histórico (OTIF) |
| :--- | :---: | :--- | :---: | :---: |
| **Telecomunicaciones y Tecnología de C.A.** | El Salvador | Electrónica y Telecom | Crédito 30 días | 96.5% |
| **Importadora San Salvador S.A. de C.V.** | El Salvador | Variedades y Tecnología | Crédito 45 días | 94.0% |
| **TechGlobal El Salvador** | El Salvador | Accesorios Digitales | Contado contra entrega | 97.2% |
| **Confecciones El Salvador S.A. de C.V.** | El Salvador | Textiles y Ropa de Trabajo | Crédito 30 días | 95.8% |
| **Textiles Cuscatlecos S.A.** | El Salvador | Calzado y Ropa Urbana | Crédito 30 días | 93.4% |
| **Distribuidora Morazán S.A. de C.V.** | El Salvador | Distribución Masiva Multisector | Crédito 60 días | 98.1% |
| **Productos Alimenticios Diana S.A. de C.V.** | El Salvador | Alimentos y Snacks | Crédito 15 días | 99.0% |
| **Industrias La Constancia** | El Salvador | Bebidas y Líquidos | Contado / Crédito 15 días | 98.5% |
| **Café Don Pedro El Salvador** | El Salvador | Café Gourmet y Derivados | Contado | 97.0% |
| **Freund S.A. de C.V.** | El Salvador | Ferretería y Materiales | Crédito 30 días | 96.0% |
| **Suministros Industriales de El Salvador** | El Salvador | Herramientas y Equipamiento | Crédito 45 días | 95.0% |
| **Indufoam El Salvador** | El Salvador | Colchones y Ropa de Cama | Crédito 30 días | 97.5% |
| **Droguería Santa Lucía S.A. de C.V.** | El Salvador | Farmacéuticos y Salud | Crédito 30 días | 98.8% |
| **Laboratorios Vijosa S.A. de C.V.** | El Salvador | Medicamentos y Dermo-salud | Crédito 30 días | 99.2% |
