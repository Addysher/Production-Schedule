import { useEffect, useState } from 'react'
import { productsApi } from '../api/admin'
import type { ProductDto } from '../types/api'

// Products are managed in Settings → Products; anything that names one picks it from this list rather
// than typing it, since a typo would match nothing.
export function ProductSelect({ value, onChange, allowNone = false }: { value: string; onChange: (name: string) => void; allowNone?: boolean }) {
  const [products, setProducts] = useState<ProductDto[]>([])

  useEffect(() => {
    productsApi.getAll().then(setProducts).catch(() => setProducts([]))
  }, [])

  // Keep the current value selectable even if that product has since been deactivated.
  const options = products.filter((p) => p.isActive || p.name === value)

  return (
    <select style={{ width: '100%' }} value={value} onChange={(e) => onChange(e.target.value)}>
      <option value="">{allowNone ? '(none)' : 'Choose a product…'}</option>
      {options.map((p) => (
        <option key={p.id} value={p.name}>
          {p.name}
          {p.isActive ? '' : ' (inactive)'}
        </option>
      ))}
    </select>
  )
}
