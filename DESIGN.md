# Notaker — identidad visual

Referencia canónica del diseño existente. El usuario pidió tema oscuro por defecto, morado y azul; esta vista conserva esa decisión.

- Aplicación WPF para Windows. Segoe UI; títulos claros, métricas tabulares legibles, explicaciones breves en español.
- Fondo `#0E1020`; superficies `#191C32`; texto `#EEF0FF`; secundario `#A5ACCB`; acento `#9775FA`; bordes `#343954`.
- Degradado principal morado `#292045` hacia azul `#172B4B`. Botón principal `#7857EC`.
- Bordes redondeados: 9 px para botones; 12–16 px para tarjetas. Espaciado interior habitual 18–28 px.
- Recursos compartidos en `src/Notaker/App.xaml`. Evitar ventanas con fondo claro y controles que ignoren estos recursos.
- Estadísticas: acceso en la barra lateral, encima de Buscar actualizaciones. Ventana redimensionable y desplazable, seis métricas principales y panel de diccionario. Las cifras cero y la ausencia de muestra tienen estados explícitos.
- No presentar aproximaciones como precisión demostrada: indicar límites del historial importado y distinguir palabras editadas, dictados retocados y términos aprendidos.
- Acciones secundarias discretas; reiniciar contadores requiere confirmación dentro de la aplicación y conserva historial/diccionario. No usar solo color para expresar errores o registro pausado.
- Verificación visual: capturas del ejecutable con estadísticas vacías y datos sintéticos; no usar dictados privados como ejemplos.

## Logo y arranque — 0.8.0

El usuario eligió la propuesta A, una N de cinta plegada con degradado morado y azul, el 19 de septiembre de 2026. Los originales transparentes de las tres propuestas están en `docs/branding/`. La N elegida es `n-ribbon.png`. Los recursos de distribución son `src/Notaker/Assets/notaker.png` y `notaker.ico`.

El icono de Windows incluye tamaños de 16, 20, 24, 32, 40, 48, 64, 128 y 256 px. PNG e ICO se derivan del original con Pillow y remuestreo Lanczos. Conservar transparencia y proporciones, sin deformarlo ni añadir fondos. Usar la misma marca en cabecera, ejecutable, barra de título y bandeja. Las ventanas secundarias heredan el icono mediante el manejador Loaded de App.

El ajuste de arranque se sitúa junto a las preferencias de dictado. Su estado se consulta en Windows al activar la ventana. Mostrar un mensaje explícito si Windows lo ha deshabilitado o la ruta corresponde a otra copia. No reemplazar la decisión del usuario en el Administrador de tareas.

### Corrección de iconos — 0.8.1

Cargar el ICO de ventanas mediante `BitmapFrame.Create`, conservando su decoder multirresolución. `BitmapImage` tomaba el frame de 16 px y WPF lo centraba dentro del HICON grande de 32 px. Medición real: N de 12 px antes y 24 px después. No modificar la marca ni compensar un error del cargador agrandando el dibujo.

La bandeja usa `Shell_NotifyIcon` con un GUID propio. El aviso normal de segundo plano usa `NIIF_USER | NIIF_LARGE_ICON` y `hBalloonIcon`, conservando símbolos semánticos para avisos de error. Se respetan las preferencias de notificaciones de Windows. Referencias: [Window.Icon](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.icon) y [NOTIFYICONDATAW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw).
