# Presets de imagen

Programa para recorrer una imagen con encuadres guardados (presets) y proyectarla en un segundo monitor con
transiciones suaves: se aleja, viaja y se vuelve a acercar (curva de van Wijk y Nuij).

C# / WPF sobre .NET 9. Sin dependencias externas.

## Ejecutar

```
dotnet run --project C:\GitHub\ImagePresets
```

Ejecutable suelto (no necesita .NET instalado):

```
dotnet publish C:\GitHub\ImagePresets -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o C:\GitHub\ImagePresets\publish
```

## Uso

1. **Abrir imagen…**: carga la imagen. Si a su lado existe `nombre.presets.json`, se abre con sus presets.
2. Encuadra en el editor: **rueda** para el zoom, **arrastrar** para mover. El **marco amarillo** es exactamente lo
   que se verá en el monitor de salida (tiene su proporción).
3. **＋ Añadir** guarda el encuadre como preset; **Actualizar** sustituye el seleccionado. El nombre se edita en la
   lista; **Subir/Bajar** cambia el orden.
4. **Guardar** (Ctrl+S) escribe `nombre-de-la-imagen.presets.json` junto a la imagen.
5. Elige el monitor de **Salida** y pulsa **Proyectar** (F5).

Durante la proyección: clic en un preset, `→` `↓` `AvPág` `Espacio` (siguiente), `←` `↑` `RePág` (anterior),
`Inicio`/`Fin`, `1`–`9`. Los mandos de presentaciones envían AvPág/RePág, así que funcionan sin configurar nada.
`Esc` en la ventana de proyección la cierra.

- **Duración**: lo que tardan los viajes largos (los cortos tardan menos).
- **Alejamiento**: cuánto se aleja la cámara al viajar (1,4 es el valor clásico).

## Archivo de presets

Las posiciones se guardan normalizadas (0–1 respecto al ancho y alto de la imagen): si cambias la imagen por otra
versión con más resolución de la misma obra, los presets siguen valiendo.

```json
{
  "image": "Tríptico del Jardín de las delicias.jpg",
  "duration": 3,
  "rho": 1.4,
  "presets": [ { "name": "Vista completa", "x": 0.5, "y": 0.5, "width": 1 } ]
}
```

## Código

- `MainWindow` — editor, lista de presets, archivos y teclado.
- `OutputWindow` — ventana de proyección a pantalla completa en el monitor elegido.
- `ImageView` — dibuja la imagen con un encuadre; en modo editor, marco, zoom y arrastre.
- `ZoomPath` — curva óptima de zoom y desplazamiento; `ViewAnimator` la recorre con arranque y frenada suaves.
- `Presentation` — preset, archivo JSON y preferencias (`%AppData%\ImagePresets\settings.json`).
- `Monitors` — monitores y colocación de ventanas con la API de Windows.
