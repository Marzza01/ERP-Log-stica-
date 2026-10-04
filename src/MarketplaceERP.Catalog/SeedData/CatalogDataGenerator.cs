/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Text;

namespace MarketplaceERP.Catalog.SeedData;

public static class CatalogDataGenerator
{
    public static void GenerarCsv(string rutaArchivo)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\"SKU\",\"Nombre del Producto\",\"Categoría\",\"Subcategoría\",\"Precio B2C ($)\",\"Precio B2B ($)\",\"Stock Inicial\",\"Stock Mínimo\",\"Proveedor\",\"País Origen\",\"Peso (kg)\",\"Dimensiones (cm)\",\"Unidad de Medida\",\"Estado\",\"Código EAN\",\"Descripción Corta\"");

        var categories = new[]
        {
            new {
                Nombre = "Electrónica y Tecnología",
                Prefijo = "ELC",
                Count = 100,
                Subcategorias = new[] { "Smartphones y Accesorios", "Computación y Laptops", "Audio y Sonido", "Cables y Conectores", "Smart Home y Redes", "Televisores y Monitores" },
                Proveedores = new[] { "Telecomunicaciones y Tecnología de C.A.", "Importadora San Salvador S.A. de C.V.", "TechGlobal El Salvador" },
                Paises = new[] { "China", "EE.UU.", "Vietnam", "Corea del Sur" },
                Items = new[] {
                    "Smartphone Pro 128GB", "Cable USB-C Carga Rápida 2m", "Auriculares Bluetooth Cancelación Ruido", "Power Bank 20000mAh",
                    "Cargador de Pared 65W GaN", "Adaptador HDMI a USB-C", "Mouse Inalámbrico Ergonómico", "Teclado Mecánico RGB",
                    "Monitor LED 24 Pulgadas FHD", "Cámara Web Full HD 1080p", "Router Wi-Fi 6 Doble Banda", "Soporte Ajustable para Laptop",
                    "Disco Sólido Externo SSD 1TB", "Memoria RAM DDR4 16GB", "Foco Inteligente Smart WiFi", "Enchufe Inteligente Smart Plug"
                }
            },
            new {
                Nombre = "Ropa y Accesorios",
                Prefijo = "ROP",
                Count = 90,
                Subcategorias = new[] { "Moda Hombre", "Moda Mujer", "Calzado Urbano", "Ropa Deportiva", "Accesorios y Bolsos", "Calzado Industrial" },
                Proveedores = new[] { "Confecciones El Salvador S.A. de C.V.", "Textiles Cuscatlecos S.A.", "Distribuidora Morazán S.A. de C.V." },
                Paises = new[] { "El Salvador", "Guatemala", "Honduras", "México" },
                Items = new[] {
                    "Camiseta Básica de Algodón Cuello Redondo", "Pantalón Denim Clásico Recto", "Polo Piqué Algodón Premium", "Camisa Formal Manga Larga Slim Fit",
                    "Chaqueta Cortavientos Liviana", "Sudadera Hoodie con Capucha", "Short Deportivo Secado Rápido", "Calcetines Tobilleros Pack 6 Pares",
                    "Zapatillas Urbanas Casual", "Botas de Trabajo con Puntera de Acero", "Cinturón de Cuero Reversible", "Mochila Ejecutiva Antirrobo Impermeable"
                }
            },
            new {
                Nombre = "Alimentos y Bebidas",
                Prefijo = "ALM",
                Count = 80,
                Subcategorias = new[] { "Granos Básicos y Despensa", "Snacks y Golosinas", "Bebidas y Jugos", "Café Salvadoreño y Té", "Salsas y Condimentos", "Conservas y Enlatados" },
                Proveedores = new[] { "Productos Alimenticios Diana S.A. de C.V.", "Industrias La Constancia", "Distribuidora Morazán S.A. de C.V.", "Café Don Pedro El Salvador" },
                Paises = new[] { "El Salvador", "Guatemala", "Nicaragua", "Costa Rica" },
                Items = new[] {
                    "Arroz Blanco Seleccionado Grano Largo 5 lb", "Frijol Rojo de Seda Salvadoreño 5 lb", "Harina de Maíz Nixtamalizada 2 lb", "Aceite Vegetal Multiuso 1L",
                    "Café Gourmet Estricta Altura Molido 400g", "Café Tradicional Tueste Oscuro 500g", "Snack Salado Variedad Diana Pack Familiar", "Galletas Dulces Surtidas Pack x12",
                    "Azúcar de Caña Refinada 1kg", "Salsa de Tomate Tradicional 200g", "Salsa Picante Criolla 150ml", "Pasta Espagueti Enriquecida 400g"
                }
            },
            new {
                Nombre = "Ferretería e Industria",
                Prefijo = "FRR",
                Count = 80,
                Subcategorias = new[] { "Herramientas Manuales", "Herramientas Eléctricas", "Pinturas y Adhesivos", "Plomería y Grifería", "Seguridad Ocupacional", "Material Eléctrico" },
                Proveedores = new[] { "Freund S.A. de C.V.", "Suministros Industriales de El Salvador", "Distribuidora Morazán S.A. de C.V." },
                Paises = new[] { "EE.UU.", "México", "China", "El Salvador" },
                Items = new[] {
                    "Martillo de Uña Curva 16oz Mango Fibra", "Juego de Destornilladores Pro 6 Piezas", "Taladro Percutor 650W con Maletín", "Esmeriladora Angular 4-1/2 Pulgadas",
                    "Cinta Métrica Profesional 5m Bloqueo", "Llave Ajustable Perico 10 Pulgadas", "Nivel de Mano Aluminio 24 Pulgadas", "Sierra Manual para Metal 12 Pulgadas",
                    "Pintura Látex Interior Blanco Galón", "Brocha para Pintura 3 Pulgadas Cerdas Suaves", "Silicona Multiuso Transparente 300ml", "Candado Laminado Acero 50mm"
                }
            },
            new {
                Nombre = "Hogar y Decoración",
                Prefijo = "HOG",
                Count = 80,
                Subcategorias = new[] { "Mobiliario y Organización", "Ropa de Cama y Colchones", "Iluminación y Decoración", "Cocina y Menaje", "Baño y Limpieza", "Jardín y Exteriores" },
                Proveedores = new[] { "Indufoam El Salvador", "Importadora San Salvador S.A. de C.V.", "Distribuidora Morazán S.A. de C.V." },
                Paises = new[] { "El Salvador", "Guatemala", "China", "Colombia" },
                Items = new[] {
                    "Juego de Sábanas Microfibra Matrimonial", "Almohada Ortopédica Memory Foam Suave", "Colchón Ortopédico Matrimonial Resortes", "Edredón Reversible Bicolor Matrimonial",
                    "Juego de Ollas Acero Inoxidable 5 Piezas", "Sartén Antiadherente Teflón 28cm", "Juego de Vajilla Cerámica 16 Piezas", "Set de Cubiertos Acero Inox 24 Piezas",
                    "Organizador Modular Plástico 4 Gavetas", "Zapatero Plegable 10 Niveles Portátil", "Cortinas Blackout Aislantes Juego 2 Paneles", "Espejo Decorativo Marco Madera 60x90cm"
                }
            },
            new {
                Nombre = "Salud y Cuidado Personal",
                Prefijo = "SAL",
                Count = 70,
                Subcategorias = new[] { "Higiene Bucal y Facial", "Cuidado del Cabello", "Botiquín y Primeros Auxilios", "Equipos Médicos del Hogar", "Dermo-cosmética", "Cuidado Infantil" },
                Proveedores = new[] { "Droguería Santa Lucía S.A. de C.V.", "Distribuidora Morazán S.A. de C.V.", "Laboratorios Vijosa S.A. de C.V." },
                Paises = new[] { "El Salvador", "México", "EE.UU.", "Costa Rica" },
                Items = new[] {
                    "Cepillo Dental Eléctrico Sónico Recargable", "Pasta Dental Protección Total 100ml Pack 3", "Enjuague Bucal Antibacterial Menta 500ml", "Shampoo Anticaspa Nutritivo 400g",
                    "Acondicionador Reparación Profunda 400ml", "Jabón Líquido Antibacterial Manos 1L", "Gel Antibacterial con Alcohol 70% 500ml", "Alcohol Isopropílico Desinfectante 70% 500ml",
                    "Termómetro Digital Infrarrojo Sin Contacto", "Tensiómetro Digital de Brazo Automático", "Oxímetro de Pulso Pantalla OLED", "Botiquín de Primeros Auxilios 35 Piezas"
                }
            }
        };

        long eanBase = 7410000000000;
        int skuCounter = 1;
        var rnd = new Random(42); // Semilla fija para reproducibilidad

        foreach (var cat in categories)
        {
            for (int i = 1; i <= cat.Count; i++)
            {
                string sku = $"{cat.Prefijo}-{i:D4}";
                string baseItem = cat.Items[(i - 1) % cat.Items.Length];
                int variante = ((i - 1) / cat.Items.Length) + 1;
                string nombre = variante > 1 ? $"{baseItem} (Mod. V{variante})" : $"{baseItem} Standard";
                string subcat = cat.Subcategorias[(i - 1) % cat.Subcategorias.Length];
                string prov = cat.Proveedores[(i - 1) % cat.Proveedores.Length];
                string pais = cat.Paises[(i - 1) % cat.Paises.Length];

                decimal precioB2C = cat.Prefijo switch
                {
                    "ELC" => Math.Round((decimal)(rnd.NextDouble() * 400 + 15), 2),
                    "ROP" => Math.Round((decimal)(rnd.NextDouble() * 70 + 10), 2),
                    "ALM" => Math.Round((decimal)(rnd.NextDouble() * 20 + 2), 2),
                    "FRR" => Math.Round((decimal)(rnd.NextDouble() * 110 + 5), 2),
                    "HOG" => Math.Round((decimal)(rnd.NextDouble() * 150 + 10), 2),
                    "SAL" => Math.Round((decimal)(rnd.NextDouble() * 50 + 3), 2),
                    _ => 25.00m
                };

                decimal precioB2B = Math.Round(precioB2C * 0.80m, 2);
                if (precioB2B < 1.0m) precioB2B = 1.0m;

                int stockMin = rnd.Next(10, 40);
                int stockIni = rnd.Next(stockMin + 10, stockMin * 5);

                decimal peso = Math.Round((decimal)(rnd.NextDouble() * 8 + 0.2), 2);
                string dimensiones = $"{rnd.Next(10, 60)}x{rnd.Next(10, 50)}x{rnd.Next(5, 30)} cm";
                string unidad = (cat.Prefijo == "ALM" && i % 3 == 0) ? "Caja x12" : (cat.Prefijo == "ROP" && i % 5 == 0) ? "Pack x3" : "Unidad";
                string estado = (i % 35 == 0) ? "Inactivo" : "Activo";
                string ean = (eanBase + skuCounter).ToString();
                skuCounter++;

                string desc = $"{nombre} de alta rotación para el mercado salvadoreño. Distribuido por {prov}.";

                sb.AppendLine($"\"{sku}\",\"{nombre}\",\"{cat.Nombre}\",\"{subcat}\",\"{precioB2C:0.00}\",\"{precioB2B:0.00}\",\"{stockIni}\",\"{stockMin}\",\"{prov}\",\"{pais}\",\"{peso:0.00}\",\"{dimensiones}\",\"{unidad}\",\"{estado}\",\"{ean}\",\"{desc}\"");
            }
        }

        string? dir = Path.GetDirectoryName(rutaArchivo);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(rutaArchivo, sb.ToString(), new UTF8Encoding(true));
    }
}
