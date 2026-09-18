# Script para generar Catalogo_500_Productos.csv con los 16 campos oficiales de LOGIMKT S.A. de C.V.
$ErrorActionPreference = "Stop"

$csvPath = Join-Path $PSScriptRoot "Catalogo_500_Productos.csv"

$categories = @(
    @{
        Nombre = "Electrónica y Tecnología"
        Prefijo = "ELC"
        Count = 100
        Subcategorias = @("Smartphones y Accesorios", "Computación y Laptops", "Audio y Sonido", "Cables y Conectores", "Smart Home y Redes", "Televisores y Monitores")
        Proveedores = @("Telecomunicaciones y Tecnología de C.A.", "Importadora San Salvador S.A. de C.V.", "TechGlobal El Salvador")
        Paises = @("China", "EE.UU.", "Vietnam", "Corea del Sur")
        Items = @(
            "Smartphone Pro 128GB", "Cable USB-C Carga Rápida 2m", "Auriculares Bluetooth Cancelación Ruido", "Power Bank 20000mAh",
            "Cargador de Pared 65W GaN", "Adaptador HDMI a USB-C", "Mouse Inalámbrico Ergonómico", "Teclado Mecánico RGB",
            "Monitor LED 24 Pulgadas FHD", "Cámara Web Full HD 1080p", "Router Wi-Fi 6 Doble Banda", "Soporte Ajustable para Laptop",
            "Disco Sólido Externo SSD 1TB", "Memoria RAM DDR4 16GB", "Foco Inteligente Smart WiFi", "Enchufe Inteligente Smart Plug",
            "Altavoz Bluetooth Resistente al Agua", "Smartwatch Deportivo Pulsómetro", "Audífonos In-Ear con Micrófono", "MicroSD 128GB Clase 10",
            "Hub USB 7 Puertos con Alimentación", "Lámpara de Escritorio LED Táctil", "Regleta Supresora de Picos 6 Tomas", "Micrófono Condensador USB Podcast",
            "Tableta Gráfica Digitalizadora", "Soporte de Celular para Automóvil", "Funda Protectora Uso Rudo", "Protector de Pantalla Cristal Templado"
        )
    },
    @{
        Nombre = "Ropa y Accesorios"
        Prefijo = "ROP"
        Count = 90
        Subcategorias = @("Moda Hombre", "Moda Mujer", "Calzado Urbano", "Ropa Deportiva", "Accesorios y Bolsos", "Calzado Industrial")
        Proveedores = @("Confecciones El Salvador S.A. de C.V.", "Textiles Cuscatlecos S.A.", "Distribuidora Morazán S.A. de C.V.")
        Paises = @("El Salvador", "Guatemala", "Honduras", "México")
        Items = @(
            "Camiseta Básica de Algodón Cuello Redondo", "Pantalón Denim Clásico Recto", "Polo Piqué Algodón Premium", "Camisa Formal Manga Larga Slim Fit",
            "Chaqueta Cortavientos Liviana", "Sudadera Hoodie con Capucha", "Short Deportivo Secado Rápido", "Calcetines Tobilleros Pack 6 Pares",
            "Zapatillas Urbanas Casual", "Botas de Trabajo con Puntera de Acero", "Cinturón de Cuero Reversible", "Mochila Ejecutiva Antirrobo Impermeable",
            "Gorra Deportiva Ajustable", "Gafas de Sol Polarizadas UV400", "Billetera de Cuero Clásica", "Vestido Casual Corte A",
            "Blusa Formal Elegante", "Leggings Deportivos Pretina Alta", "Pijama Dos Piezas Algodón", "Ropa Interior Boxer Algodón Pack 3"
        )
    },
    @{
        Nombre = "Alimentos y Bebidas"
        Prefijo = "ALM"
        Count = 80
        Subcategorias = @("Granos Básicos y Despensa", "Snacks y Golosinas", "Bebidas y Jugos", "Café Salvadoreño y Té", "Salsas y Condimentos", "Conservas y Enlatados")
        Proveedores = @("Productos Alimenticios Diana S.A. de C.V.", "Industrias La Constancia", "Distribuidora Morazán S.A. de C.V.", "Café Don Pedro El Salvador")
        Paises = @("El Salvador", "Guatemala", "Nicaragua", "Costa Rica")
        Items = @(
            "Arroz Blanco Seleccionado Grano Largo 5 lb", "Frijol Rojo de Seda Salvadoreño 5 lb", "Harina de Maíz Nixtamalizada 2 lb", "Aceite Vegetal Multiuso 1L",
            "Café Gourmet Estricta Altura Molido 400g", "Café Tradicional Tueste Oscuro 500g", "Snack Salado Variedad Diana Pack Familiar", "Galletas Dulces Surtidas Pack x12",
            "Azúcar de Caña Refinada 1kg", "Salsa de Tomate Tradicional 200g", "Salsa Picante Criolla 150ml", "Pasta Espagueti Enriquecida 400g",
            "Atún en Trozos en Aceite 140g", "Sardina en Salsa de Tomate 155g", "Jugo Néctar Melocotón 1L", "Agua Purificada Embotellada Pack 24 x 500ml",
            "Leche Entera UHT Larga Vida 1L", "Avena en Hojuelas Integral 800g", "Especias y Sazonador Mixto 100g", "Miel de Abeja Pura 500g"
        )
    },
    @{
        Nombre = "Ferretería e Industria"
        Prefijo = "FRR"
        Count = 80
        Subcategorias = @("Herramientas Manuales", "Herramientas Eléctricas", "Pinturas y Adhesivos", "Plomería y Grifería", "Seguridad Ocupacional", "Material Eléctrico")
        Proveedores = @("Freund S.A. de C.V.", "Suministros Industriales de El Salvador", "Distribuidora Morazán S.A. de C.V.")
        Paises = @("EE.UU.", "México", "China", "El Salvador")
        Items = @(
            "Martillo de Uña Curva 16oz Mango Fibra", "Juego de Destornilladores Pro 6 Piezas", "Taladro Percutor 650W con Maletín", "Esmeriladora Angular 4-1/2 Pulgadas",
            "Cinta Métrica Profesional 5m Bloqueo", "Llave Ajustable Perico 10 Pulgadas", "Nivel de Mano Aluminio 24 Pulgadas", "Sierra Manual para Metal 12 Pulgadas",
            "Pintura Látex Interior Blanco Galón", "Brocha para Pintura 3 Pulgadas Cerdas Suaves", "Silicona Multiuso Transparente 300ml", "Pegamento de Contacto Fuerte 250ml",
            "Cinta Aislante Eléctrica Negra 20m", "Cerradura de Sobreponer Seguridad Cilindro", "Candado Laminado Acero 50mm", "Guantes de Nitrilo Resistencia Química Par",
            "Casco de Seguridad Industrial con Barboquejo", "Gafas de Protección Anti-empañante", "Extensión Eléctrica Industrial Uso Rudo 10m", "Tubo PVC Presión 1/2 Pulgada x 6m"
        )
    },
    @{
        Nombre = "Hogar y Decoración"
        Prefijo = "HOG"
        Count = 80
        Subcategorias = @("Mobiliario y Organización", "Ropa de Cama y Colchones", "Iluminación y Decoración", "Cocina y Menaje", "Baño y Limpieza", "Jardín y Exteriores")
        Proveedores = @("Indufoam El Salvador", "Importadora San Salvador S.A. de C.V.", "Distribuidora Morazán S.A. de C.V.")
        Paises = @("El Salvador", "Guatemala", "China", "Colombia")
        Items = @(
            "Juego de Sábanas Microfibra Matrimonial", "Almohada Ortopédica Memory Foam Suave", "Colchón Ortopédico Matrimonial Resortes", "Edredón Reversible Bicolor Matrimonial",
            "Juego de Ollas Acero Inoxidable 5 Piezas", "Sartén Antiadherente Teflón 28cm", "Juego de Vajilla Cerámica 16 Piezas", "Set de Cubiertos Acero Inox 24 Piezas",
            "Organizador Modular Plástico 4 Gavetas", "Zapatero Plegable 10 Niveles Portátil", "Cortinas Blackout Aislantes Juego 2 Paneles", "Espejo Decorativo Marco Madera 60x90cm",
            "Toalla de Baño Algodón 550 GSM", "Dispensador de Jabón Automático con Sensor", "Cesto para Ropa Plegable de Tela", "Aspiradora Manual Ciclónica 600W",
            "Manguera Retráctil para Jardín 15m", "Set de Herramientas de Jardinería 4 Piezas", "Maceta Decorativa Cerámica Blanca 25cm", "Lámpara Colgante Vintage Techo"
        )
    },
    @{
        Nombre = "Salud y Cuidado Personal"
        Prefijo = "SAL"
        Count = 70
        Subcategorias = @("Higiene Bucal y Facial", "Cuidado del Cabello", "Botiquín y Primeros Auxilios", "Equipos Médicos del Hogar", "Dermo-cosmética", "Cuidado Infantil")
        Proveedores = @("Droguería Santa Lucía S.A. de C.V.", "Distribuidora Morazán S.A. de C.V.", "Laboratorios Vijosa S.A. de C.V.")
        Paises = @("El Salvador", "México", "EE.UU.", "Costa Rica")
        Items = @(
            "Cepillo Dental Eléctrico Sónico Recargable", "Pasta Dental Protección Total 100ml Pack 3", "Enjuague Bucal Antibacterial Menta 500ml", "Shampoo Anticaspa Nutritivo 400g",
            "Acondicionador Reparación Profunda 400ml", "Jabón Líquido Antibacterial Manos 1L", "Gel Antibacterial con Alcohol 70% 500ml", "Alcohol Isopropílico Desinfectante 70% 500ml",
            "Termómetro Digital Infrarrojo Sin Contacto", "Tensiómetro Digital de Brazo Automático", "Oxímetro de Pulso Pantalla OLED", "Botiquín de Primeros Auxilios 35 Piezas",
            "Gasas Estériles y Esparadrapo Médico Pack", "Protector Solar FPS 50+ Facial No Graso 50ml", "Crema Hidratante Piel Seca Ceramidas 250ml", "Toallitas Húmedas Hipoalergénicas Pack 80",
            "Pañales Talla Grande Pack Ahorro 60 Unidades", "Suero Rehidratante Oral Sabores Pack 4", "Vitaminas y Minerales Complejo B 60 Cápsulas", "Vitamina C 1000mg Efervescente 20 Tabletas"
        )
    }
)

$allProducts = @()
$eanBase = 7410000000000 # Prefijo EAN para Centroamérica/El Salvador
$skuCounter = 1

foreach ($cat in $categories) {
    $catItems = $cat.Items
    $catSubs = $cat.Subcategorias
    $catProvs = $cat.Proveedores
    $catPaises = $cat.Paises
    
    for ($i = 1; $i -le $cat.Count; $i++) {
        $sku = "{0}-{1:D4}" -f $cat.Prefijo, $i
        $baseItemName = $catItems[($i - 1) % $catItems.Count]
        $subcat = $catSubs[($i - 1) % $catSubs.Count]
        $prov = $catProvs[($i - 1) % $catProvs.Count]
        $pais = $catPaises[($i - 1) % $catPaises.Count]

        # Variación de modelo / lote para realismo
        $variante = [math]::Ceiling($i / $catItems.Count)
        if ($variante -gt 1) {
            $nombre = "$baseItemName (Mod. V$variante)"
        } else {
            $nombre = "$baseItemName Standard"
        }

        # Precios
        $precioB2C = 0
        switch ($cat.Prefijo) {
            "ELC" { $precioB2C = [math]::Round((Get-Random -Minimum 8.0 -Maximum 450.0), 2) }
            "ROP" { $precioB2C = [math]::Round((Get-Random -Minimum 10.0 -Maximum 85.0), 2) }
            "ALM" { $precioB2C = [math]::Round((Get-Random -Minimum 1.5 -Maximum 22.0), 2) }
            "FRR" { $precioB2C = [math]::Round((Get-Random -Minimum 5.0 -Maximum 130.0), 2) }
            "HOG" { $precioB2C = [math]::Round((Get-Random -Minimum 8.0 -Maximum 180.0), 2) }
            "SAL" { $precioB2C = [math]::Round((Get-Random -Minimum 3.5 -Maximum 65.0), 2) }
        }
        
        # B2B tiene descuento por volumen (15% a 30% menos)
        $descRatio = (Get-Random -Minimum 75 -Maximum 85) / 100.0
        $precioB2B = [math]::Round($precioB2C * $descRatio, 2)
        if ($precioB2B -lt 1.0) { $precioB2B = 1.0 }

        # Stock
        $stockMin = Get-Random -Minimum 10 -Maximum 50
        $stockIni = Get-Random -Minimum ($stockMin - 5) -Maximum ($stockMin * 6)
        if ($stockIni -lt 0) { $stockIni = 5 }

        # Peso y Dimensiones
        $peso = [math]::Round((Get-Random -Minimum 0.1 -Maximum 12.0), 2)
        $dimL = Get-Random -Minimum 10 -Maximum 80
        $dimW = Get-Random -Minimum 10 -Maximum 60
        $dimH = Get-Random -Minimum 5 -Maximum 40
        $dimensiones = "${dimL}x${dimW}x${dimH} cm"

        # EAN-13
        $ean = ($eanBase + $skuCounter).ToString()
        $skuCounter++

        # Unidad
        $unidad = "Unidad"
        if ($cat.Prefijo -eq "ALM" -and ($i % 3 -eq 0)) { $unidad = "Caja x12" }
        elseif ($cat.Prefijo -eq "ROP" -and ($i % 5 -eq 0)) { $unidad = "Pack x3" }

        # Estado
        $estado = if ($i % 30 -eq 0) { "Inactivo" } else { "Activo" }

        $desc = "$nombre para el mercado salvadoreño. Calidad garantizada distribuido por $prov."

        $allProducts += [PSCustomObject]@{
            "SKU" = $sku
            "Nombre del Producto" = $nombre
            "Categoría" = $cat.Nombre
            "Subcategoría" = $subcat
            "Precio B2C ($)" = ("{0:N2}" -f $precioB2C).Replace(",", ".")
            "Precio B2B ($)" = ("{0:N2}" -f $precioB2B).Replace(",", ".")
            "Stock Inicial" = $stockIni
            "Stock Mínimo" = $stockMin
            "Proveedor" = $prov
            "País Origen" = $pais
            "Peso (kg)" = ("{0:N2}" -f $peso).Replace(",", ".")
            "Dimensiones (cm)" = $dimensiones
            "Unidad de Medida" = $unidad
            "Estado" = $estado
            "Código EAN" = $ean
            "Descripción Corta" = $desc
        }
    }
}

# Exportar CSV con codificación UTF-8 con BOM para perfecta apertura en Excel y C#
$csvContent = $allProducts | ConvertTo-Csv -NoTypeInformation
$utf8WithBom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllLines($csvPath, $csvContent, $utf8WithBom)

Write-Host "Catálogo generado exitosamente en $csvPath con $($allProducts.Count) productos (UTF-8 con BOM)."
