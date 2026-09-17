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
